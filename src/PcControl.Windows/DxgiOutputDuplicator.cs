using System.Runtime.InteropServices;
using PcControl.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace PcControl.Windows;

/// <summary>
/// Watches one monitor with DXGI Desktop Duplication: Windows tells us when the picture changes, and
/// hands over the finished image from the graphics card. Unlike GDI screen capture it does not touch
/// the mouse cursor, and when nothing on screen changes it does no work at all. Not thread safe; the
/// owner serialises calls.
/// </summary>
/// <summary>Counts of what the mouse pointer did, as Windows reported it.</summary>
internal readonly record struct PointerStatistics(int Updates, int VisibilityFlips, int ShapeChanges);

internal sealed class DxgiOutputDuplicator : IDisposable
{
    private const int WaitTimeout = unchecked((int)0x887A0027); // DXGI_ERROR_WAIT_TIMEOUT
    private const int AccessLost = unchecked((int)0x887A0026); // DXGI_ERROR_ACCESS_LOST

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _frame;
    private ID3D11Texture2D? _staging;
    private ID3D11ShaderResourceView? _frameView;
    private int _mipLevels = 1;
    private long _mipsVersion = -1;
    private (int Width, int Height) _stagingSize;
    private bool _hasFrame;

    // The last picture handed out. If the screen has not changed inside the area it shows, it is
    // handed out again as it is, so a still part of the screen costs nothing to ask for.
    private readonly ChangeLog _changes = new();
    // One entry per distinct request (the mini and the display preview ask for different things,
    // and must not push each other out).
    private const int MaxCachedRequests = 8;
    private readonly Dictionary<(Rect Region, int Width, int Height), (long Version, RawFrame Frame)> _cache = [];

    private DxgiOutputDuplicator(Rect desktop)
    {
        Desktop = desktop;
    }

    /// <summary>The monitor's place on the desktop, as the user sees it.</summary>
    public Rect Desktop { get; }

    /// <summary>
    /// How the duplicated image is turned relative to the monitor as the user sees it. Graphics
    /// drivers differ: on a rotated panel some hand over the picture already upright, others in the
    /// panel's own orientation, and what they *say* about it is not always what they do. So this is
    /// decided from the size of the first real picture (see <see cref="Refresh"/>).
    /// </summary>
    public OutputRotation Rotation { get; private set; } = OutputRotation.Identity;

    /// <summary>The rotation the duplication claims; only used when the picture's size is consistent with it.</summary>
    private OutputRotation _claimedRotation = OutputRotation.Identity;

    /// <summary>Goes up each time the picture on this monitor changed anywhere.</summary>
    public long Version => _changes.Version;

    /// <summary>
    /// What Windows reported about the mouse pointer through this duplication: how many times it was
    /// moved, shown or hidden, or changed shape. Only for diagnostics (is something making the
    /// pointer flicker?); it costs nothing because Windows sends these with every frame anyway.
    /// </summary>
    public PointerStatistics Pointer { get; private set; }

    private int? _lastPointerVisible;

    /// <summary>True once Windows has taken the duplication away (a lock screen, a resolution change); make a new one.</summary>
    public bool Lost { get; private set; }

    /// <summary>True when the pictures this monitor produces are in a form we cannot read (the capturer then falls back).</summary>
    public bool Unsupported { get; private set; }

    public static unsafe DxgiOutputDuplicator? TryCreate(IDXGIAdapter1 adapter, IDXGIOutput output, out string? problem)
    {
        problem = null;
        var desc = output.GetDesc();
        var desktop = new Rect(
            desc.DesktopCoordinates.left,
            desc.DesktopCoordinates.top,
            desc.DesktopCoordinates.right - desc.DesktopCoordinates.left,
            desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top);
        var result = new DxgiOutputDuplicator(desktop);
        try
        {
            var hr = PInvoke.D3D11CreateDevice(
                adapter,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                HMODULE.Null,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                default,
                PInvoke.D3D11_SDK_VERSION,
                out var device,
                out var context);
            if (hr.Failed)
            {
                problem = $"could not create a graphics device (0x{hr.Value:X8})";
                result.Dispose();
                return null;
            }

            result._device = device;
            result._context = context;

            hr = ((IDXGIOutput1)output).DuplicateOutput(device, out var duplication);
            if (hr.Failed)
            {
                problem = $"Windows refused screen duplication for {desc.DeviceName} (0x{hr.Value:X8})";
                result.Dispose();
                return null;
            }

            result._duplication = duplication;

            DXGI_OUTDUPL_DESC duplicationDesc;
            duplication.GetDesc(&duplicationDesc);
            if (duplicationDesc.ModeDesc.Format != DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM || duplicationDesc.DesktopImageInSystemMemory)
            {
                problem = $"unsupported screen format {duplicationDesc.ModeDesc.Format} (HDR or a software surface)";
                result.Dispose();
                return null;
            }

            result._claimedRotation = (int)duplicationDesc.Rotation is >= 1 and <= 4 ? (OutputRotation)(int)duplicationDesc.Rotation : OutputRotation.Identity;
            return result;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            problem = ex.Message;
            result.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Takes in whatever the screen has done since the last call. Returns false when the duplication
    /// is gone and must be recreated. <paramref name="firstFrameTimeoutMs"/> is only used until the
    /// first picture has arrived.
    /// </summary>
    public unsafe bool Refresh(uint firstFrameTimeoutMs)
    {
        if (_duplication is null || _context is null)
        {
            return false;
        }

        DXGI_OUTDUPL_FRAME_INFO info;
        var hr = _duplication.AcquireNextFrame(_hasFrame ? 0u : firstFrameTimeoutMs, &info, out var resource);

        if (hr.Value == WaitTimeout)
        {
            return true; // nothing changed
        }

        if (hr.Failed)
        {
            Lost = hr.Value == AccessLost || Lost;
            return false;
        }

        NotePointer(info);

        try
        {
            // A frame with no present time only moved the mouse pointer; the picture is the same. The
            // very first frame is the exception: it is the picture as it is right now, usually with no
            // present time at all, and it must be kept or a still desktop would never get a first picture.
            if (info.LastPresentTime != 0 || !_hasFrame)
            {
                var picture = (ID3D11Texture2D)resource;
                if (_frame is null && !CreateFrameTextureFor(picture))
                {
                    Unsupported = true;
                    return false;
                }

                _context.CopySubresourceRegion(_frame!, 0, 0, 0, 0, picture, 0, null);
                _hasFrame = true;
                _changes.Record(ReadChangedAreas(info));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(resource);
            _duplication.ReleaseFrame();
        }

        return true;
    }

    /// <summary>
    /// The part of this monitor in <paramref name="region"/> (monitor coordinates, as seen), scaled to
    /// the output size. Returns the very same object as last time when nothing changed since.
    /// </summary>
    public unsafe RawFrame? Read(Rect region, int outputWidth, int outputHeight)
    {
        if (!_hasFrame || _context is null || _device is null || _frame is null)
        {
            return null;
        }

        var box = OutputGeometry.ImageBox(Rotation, region, Desktop.Width, Desktop.Height);

        // The same area at the same size as last time, and nothing inside it has changed since:
        // hand back the very same picture. (The streamer then knows it has nothing new to send.)
        var requestKey = (region, outputWidth, outputHeight);
        if (_cache.TryGetValue(requestKey, out var last) && !_changes.ChangedSince(last.Version, box))
        {
            _cache[requestKey] = (Version, last.Frame);
            return last.Frame;
        }

        var version = Version;
        // Start from a smaller copy of the screen when the output is much smaller than the area.
        var level = LevelFor(box, outputWidth, outputHeight);
        if (level > 0 && _mipsVersion != Version)
        {
            _context.GenerateMips(_frameView!);
            _mipsVersion = Version;
        }

        var scaledBox = level == 0 ? box : ShrinkBox(box, level);
        var staging = StagingFor(scaledBox.Width, scaledBox.Height);

        var source = new D3D11_BOX
        {
            left = (uint)scaledBox.X,
            top = (uint)scaledBox.Y,
            front = 0,
            right = (uint)scaledBox.Right,
            bottom = (uint)scaledBox.Bottom,
            back = 1,
        };
        _context.CopySubresourceRegion(staging, 0, 0, 0, 0, _frame, (uint)level, &source);

        D3D11_MAPPED_SUBRESOURCE mapped;
        _context.Map(staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
        try
        {
            var pixels = new byte[outputWidth * outputHeight * 4];
            if (level > 0)
            {
                ResampleUpright((byte*)mapped.pData, (int)mapped.RowPitch, new Rect(0, 0, scaledBox.Width, scaledBox.Height), outputWidth, outputHeight, pixels);
            }
            else
            {
                Resample((byte*)mapped.pData, (int)mapped.RowPitch, box, region, outputWidth, outputHeight, pixels);
            }
            var made = new RawFrame(outputWidth, outputHeight, pixels);
            if (_cache.Count >= MaxCachedRequests && !_cache.ContainsKey(requestKey))
            {
                _cache.Clear();
            }

            _cache[requestKey] = (version, made);
            return made;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    private void NotePointer(DXGI_OUTDUPL_FRAME_INFO info)
    {
        var updates = Pointer.Updates;
        var flips = Pointer.VisibilityFlips;

        if (info.LastMouseUpdateTime != 0)
        {
            updates++;
            var visible = info.PointerPosition.Visible ? 1 : 0;
            if (_lastPointerVisible is { } before && before != visible)
            {
                flips++;
            }

            _lastPointerVisible = visible;
        }

        Pointer = new PointerStatistics(updates, flips, Pointer.ShapeChanges + (info.PointerShapeBufferSize != 0 ? 1 : 0));
    }

    /// <summary>
    /// The parts of the picture that differ from the one before, in the picture's own coordinates:
    /// what Windows calls dirty rectangles, plus where things were moved to (scrolling). Null when
    /// Windows gives no usable answer, which means "assume all of it changed".
    /// </summary>
    private unsafe List<Rect>? ReadChangedAreas(DXGI_OUTDUPL_FRAME_INFO info)
    {
        if (_duplication is null || info.TotalMetadataBufferSize == 0)
        {
            return null;
        }

        try
        {
            var areas = new List<Rect>();
            var capacity = (int)info.TotalMetadataBufferSize;

            var dirty = new RECT[(capacity / sizeof(RECT)) + 1];
            uint required;
            fixed (RECT* p = dirty)
            {
                _duplication.GetFrameDirtyRects((uint)(dirty.Length * sizeof(RECT)), p, out required);
            }

            for (var i = 0; i < required / sizeof(RECT); i++)
            {
                areas.Add(new Rect(dirty[i].left, dirty[i].top, dirty[i].right - dirty[i].left, dirty[i].bottom - dirty[i].top));
            }

            var moves = new DXGI_OUTDUPL_MOVE_RECT[(capacity / sizeof(DXGI_OUTDUPL_MOVE_RECT)) + 1];
            fixed (DXGI_OUTDUPL_MOVE_RECT* p = moves)
            {
                _duplication.GetFrameMoveRects((uint)(moves.Length * sizeof(DXGI_OUTDUPL_MOVE_RECT)), p, out required);
            }

            for (var i = 0; i < required / sizeof(DXGI_OUTDUPL_MOVE_RECT); i++)
            {
                var d = moves[i].DestinationRect;
                areas.Add(new Rect(d.left, d.top, d.right - d.left, d.bottom - d.top));
            }

            return areas;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the working copy from the first real picture and settles the rotation from its size:
    /// the size of the monitor as seen means the picture is already upright; the size with width and
    /// height swapped means it is in the panel's own orientation and the claimed rotation applies.
    /// </summary>
    private unsafe bool CreateFrameTextureFor(ID3D11Texture2D picture)
    {
        D3D11_TEXTURE2D_DESC actual;
        picture.GetDesc(&actual);

        if (actual.Format != DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM)
        {
            return false;
        }

        var size = ((int)actual.Width, (int)actual.Height);
        if (size == (Desktop.Width, Desktop.Height))
        {
            Rotation = OutputRotation.Identity;
        }
        else if (size == OutputGeometry.ImageSize(_claimedRotation, Desktop.Width, Desktop.Height)
                 && _claimedRotation is OutputRotation.Rotate90 or OutputRotation.Rotate270)
        {
            Rotation = _claimedRotation;
        }
        else
        {
            return false;
        }

        // An upright picture gets a chain of ever smaller copies (made by the graphics card), so that
        // shrinking a whole screen for the phone starts from a much smaller picture. Reading the
        // full-size picture from system memory is the slow part otherwise.
        var mips = Rotation == OutputRotation.Identity;
        _frame = CreateTexture(_device!, size.Item1, size.Item2, D3D11_USAGE.D3D11_USAGE_DEFAULT, cpuAccess: 0, withMips: mips);
        if (mips)
        {
            try
            {
                _device!.CreateShaderResourceView(_frame, null, out _frameView);
                _mipLevels = 1 + (int)Math.Floor(Math.Log2(Math.Min(size.Item1, size.Item2)));
            }
            catch (COMException)
            {
                _frameView = null; // no mip chain; the full-size picture is used
            }
        }

        return true;
    }

    /// <summary>How many halvings of the picture to start from: as many as still leave it at least as big as the output.</summary>
    private int LevelFor(Rect box, int outputWidth, int outputHeight)
    {
        if (_frameView is null || Rotation != OutputRotation.Identity)
        {
            return 0;
        }

        var ratio = Math.Min(box.Width / (double)outputWidth, box.Height / (double)outputHeight);
        return ratio < 2 ? 0 : Math.Min((int)Math.Floor(Math.Log2(ratio)), _mipLevels - 1);
    }

    /// <summary>The same area in the <paramref name="level"/>th smaller copy, rounded outwards.</summary>
    private Rect ShrinkBox(Rect box, int level)
    {
        var levelWidth = Math.Max(1, Desktop.Width >> level);
        var levelHeight = Math.Max(1, Desktop.Height >> level);
        var left = Math.Clamp(box.X >> level, 0, levelWidth - 1);
        var top = Math.Clamp(box.Y >> level, 0, levelHeight - 1);
        var right = Math.Clamp((box.Right + (1 << level) - 1) >> level, left + 1, levelWidth);
        var bottom = Math.Clamp((box.Bottom + (1 << level) - 1) >> level, top + 1, levelHeight);
        return new Rect(left, top, right - left, bottom - top);
    }

    private unsafe void Resample(byte* source, int pitch, Rect box, Rect region, int outputWidth, int outputHeight, byte[] destination)
    {
        if (Rotation == OutputRotation.Identity)
        {
            ResampleUpright(source, pitch, region, outputWidth, outputHeight, destination);
        }
        else
        {
            ResampleRotated(source, pitch, box, region, outputWidth, outputHeight, destination);
        }
    }

    /// <summary>
    /// The common case, and the fast one: the picture is upright, so each sample position becomes a
    /// plain byte offset worked out once, and the inner loop is nothing but reads and additions. A
    /// whole 3440 x 1440 screen shrunk for the phone takes a few milliseconds this way.
    /// </summary>
    private static unsafe void ResampleUpright(byte* source, int pitch, Rect region, int outputWidth, int outputHeight, byte[] destination)
    {
        var samplesX = OutputGeometry.SamplesFor(region.Width, outputWidth);
        var samplesY = OutputGeometry.SamplesFor(region.Height, outputHeight);
        var columns = OutputGeometry.SamplePositions(region.Width, outputWidth, samplesX);
        var rows = OutputGeometry.SamplePositions(region.Height, outputHeight, samplesY);

        // Offsets from the start of the copied area (it starts at the region's top-left corner).
        var columnOffsets = new int[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            columnOffsets[i] = columns[i] * 4;
        }

        var rowOffsets = new long[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            rowOffsets[i] = (long)rows[i] * pitch;
        }

        var count = samplesX * samplesY;
        var reciprocal = (1 << 16) / count; // divide by multiplying and shifting; cheaper than dividing per pixel

        fixed (byte* output = destination)
        fixed (int* cols = columnOffsets)
        fixed (long* rowOffs = rowOffsets)
        {
            var write = output;
            for (var oy = 0; oy < outputHeight; oy++)
            {
                var rowBase = rowOffs + (oy * samplesY);
                for (var ox = 0; ox < outputWidth; ox++)
                {
                    var colBase = cols + (ox * samplesX);
                    int b = 0, g = 0, r = 0;
                    for (var ky = 0; ky < samplesY; ky++)
                    {
                        var row = source + rowBase[ky];
                        for (var kx = 0; kx < samplesX; kx++)
                        {
                            var pixel = row + colBase[kx];
                            b += pixel[0];
                            g += pixel[1];
                            r += pixel[2];
                        }
                    }

                    write[0] = (byte)((b * reciprocal) >> 16);
                    write[1] = (byte)((g * reciprocal) >> 16);
                    write[2] = (byte)((r * reciprocal) >> 16);
                    write[3] = 255;
                    write += 4;
                }
            }
        }
    }

    /// <summary>
    /// A picture that arrives turned (a portrait monitor whose graphics card hands over the panel's own
    /// orientation). Where a sample lives in memory is a straight sum of a part that depends only on
    /// its column on screen and a part that depends only on its row, so both are worked out once and
    /// the inner loop is as plain as the upright one.
    /// </summary>
    private unsafe void ResampleRotated(byte* source, int pitch, Rect box, Rect region, int outputWidth, int outputHeight, byte[] destination)
    {
        var samplesX = OutputGeometry.SamplesFor(region.Width, outputWidth);
        var samplesY = OutputGeometry.SamplesFor(region.Height, outputHeight);
        var xs = OutputGeometry.SamplePositions(region.Width, outputWidth, samplesX);
        var ys = OutputGeometry.SamplePositions(region.Height, outputHeight, samplesY);

        long OffsetOf(int x, int y)
        {
            var (ix, iy) = OutputGeometry.ToImage(Rotation, region.X + x, region.Y + y, Desktop.Width, Desktop.Height);
            return ((long)(iy - box.Y) * pitch) + ((long)(ix - box.X) * 4);
        }

        var origin = OffsetOf(0, 0);
        var columnTerms = new long[xs.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            columnTerms[i] = OffsetOf(xs[i], 0) - origin;
        }

        var rowTerms = new long[ys.Length];
        for (var i = 0; i < ys.Length; i++)
        {
            rowTerms[i] = OffsetOf(0, ys[i]) - origin;
        }

        var reciprocal = (1 << 16) / (samplesX * samplesY);

        fixed (byte* output = destination)
        fixed (long* cols = columnTerms)
        fixed (long* rowTs = rowTerms)
        {
            var write = output;
            var start = source + origin;
            for (var oy = 0; oy < outputHeight; oy++)
            {
                var rowBase = rowTs + (oy * samplesY);
                for (var ox = 0; ox < outputWidth; ox++)
                {
                    var colBase = cols + (ox * samplesX);
                    int b = 0, g = 0, r = 0;
                    for (var ky = 0; ky < samplesY; ky++)
                    {
                        var row = start + rowBase[ky];
                        for (var kx = 0; kx < samplesX; kx++)
                        {
                            var pixel = row + colBase[kx];
                            b += pixel[0];
                            g += pixel[1];
                            r += pixel[2];
                        }
                    }

                    write[0] = (byte)((b * reciprocal) >> 16);
                    write[1] = (byte)((g * reciprocal) >> 16);
                    write[2] = (byte)((r * reciprocal) >> 16);
                    write[3] = 255;
                    write += 4;
                }
            }
        }
    }

    private ID3D11Texture2D StagingFor(int width, int height)
    {
        if (_staging is not null && _stagingSize == (width, height))
        {
            return _staging;
        }

        ReleaseCom(ref _staging);
        _staging = CreateTexture(_device!, width, height, D3D11_USAGE.D3D11_USAGE_STAGING, cpuAccess: D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ);
        _stagingSize = (width, height);
        return _staging;
    }

    private static unsafe ID3D11Texture2D CreateTexture(ID3D11Device device, int width, int height, D3D11_USAGE usage, D3D11_CPU_ACCESS_FLAG cpuAccess, bool withMips = false)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = withMips ? 0u : 1u,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = usage,
            CPUAccessFlags = cpuAccess,
            BindFlags = withMips ? D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET : 0,
            MiscFlags = withMips ? D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_GENERATE_MIPS : 0,
        };

        ID3D11Texture2D_unmanaged* raw;
        device.CreateTexture2D(&desc, null, &raw);
        try
        {
            return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)raw);
        }
        finally
        {
            Marshal.Release((nint)raw); // GetObjectForIUnknown took its own reference
        }
    }

    public void Dispose()
    {
        ReleaseCom(ref _staging);
        ReleaseCom(ref _frameView);
        ReleaseCom(ref _frame);
        ReleaseCom(ref _duplication);
        ReleaseCom(ref _context);
        ReleaseCom(ref _device);
        _cache.Clear();
    }

    private static void ReleaseCom<T>(ref T? comObject)
        where T : class
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }

        comObject = null;
    }
}
