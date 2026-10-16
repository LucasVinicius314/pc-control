using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using PcControl.Core.Performance;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;

namespace PcControl.Windows;

/// <summary>
/// Reads CPU, memory, GPU, disk and network use the way Task Manager does: processor times and I/O
/// counts from Windows, and the "GPU Engine" and "PhysicalDisk" performance counters. Every part that
/// can be missing (no GPU counters, no access to a program) is left out instead of failing.
/// </summary>
public sealed class PerformanceSampler : IPerformanceSource, IDisposable
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private bool _primed;
    private TimeSpan _previousAt;
    private PerformanceCounter? _cpu;
    private bool _cpuTried;
    private Dictionary<long, (TimeSpan Cpu, ulong Io)> _previousProcesses = [];
    private HashSet<int> _unreadable = [];
    private (long Received, long Sent)? _previousNetwork;
    private Dictionary<string, CounterSample> _previousGpu = [];

    private PerformanceCounter? _diskIdle;
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;
    private bool _diskTried;

    public PerformanceSnapshot Read()
    {
        lock (_gate)
        {
            // A reading compares with the one before it. With none, or one from a long time ago (nobody was
            // looking), that would average over minutes, so take a fresh baseline and look again shortly.
            if (!_primed || _clock.Elapsed - _previousAt > TimeSpan.FromSeconds(6))
            {
                Measure();
                Thread.Sleep(350);
                _primed = true;
            }

            return Measure();
        }
    }

    private PerformanceSnapshot Measure()
    {
        var now = _clock.Elapsed;
        var seconds = Math.Max(0.05, (now - _previousAt).TotalSeconds);
        _previousAt = now;
        var cores = Environment.ProcessorCount;

        var cpu = ReadCpu();
        ReadMemory(out var used, out var total);
        var gpuByProcess = ReadGpu(out var gpuTotal);
        var processes = ReadProcesses(seconds, cores, gpuByProcess);
        var (disk, read, write) = ReadDisk();
        var (down, up) = ReadNetwork(seconds);

        return new PerformanceSnapshot(cpu, cores, used, total, gpuTotal, disk, read, write, down, up, processes);
    }

    // ---- CPU and memory ----------------------------------------------------------------------

    private double ReadCpu()
    {
        if (!_cpuTried)
        {
            _cpuTried = true;
            try
            {
                // "Utility" is what Task Manager shows; older Windows only has "Processor Time".
                _cpu = PerformanceCounterCategory.CounterExists("% Processor Utility", "Processor Information")
                    ? new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true)
                    : new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                _cpu = null;
            }
        }

        try
        {
            return _cpu is null ? 0 : Math.Clamp(_cpu.NextValue(), 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return 0;
        }
    }

    private static void ReadMemory(out long used, out long total)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!PInvoke.GlobalMemoryStatusEx(ref status))
        {
            used = 0;
            total = 0;
            return;
        }

        total = (long)status.ullTotalPhys;
        used = total - (long)status.ullAvailPhys;
    }

    // ---- programs ----------------------------------------------------------------------------

    private List<ProcessUsage> ReadProcesses(double seconds, int cores, Dictionary<int, double> gpuByProcess)
    {
        var current = new Dictionary<long, (TimeSpan, ulong)>();
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var stillUnreadable = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                // Every program is listed. Windows keeps some of its own (and some security software) from being
                // measured without administrator rights; those still show up, with the figures that can be read.
                string name;
                int id;
                try
                {
                    name = process.ProcessName;
                    id = process.Id;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    continue; // exited meanwhile
                }

                if (id == 0)
                {
                    continue; // the idle "process" is not a program
                }

                // Windows refuses access to some programs (its own, security software). Asking costs an exception
                // each time, so a program that refused is remembered and not asked again while it keeps running.
                long key = HashCode.Combine(id, 0L);
                TimeSpan? cpuTime = null;
                ulong io = 0;
                if (!_unreadable.Contains(id))
                {
                    if (TryReadHandleBased(process, out var readKey, out var readCpu, out var readIo))
                    {
                        key = readKey;
                        cpuTime = readCpu;
                        io = readIo;
                    }
                    else
                    {
                        _unreadable.Add(id);
                    }
                }

                if (_unreadable.Contains(id))
                {
                    stillUnreadable.Add(id);
                }

                long memory = 0;
                try
                {
                    memory = process.WorkingSet64; // from the process list itself: needs no access to the program
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Gone meanwhile.
                }

                if (cpuTime is { } measured)
                {
                    current[key] = (measured, io);
                }

                double cpu = 0;
                double ioRate = 0;
                if (cpuTime is { } now && _previousProcesses.TryGetValue(key, out var before))
                {
                    cpu = Math.Max(0, (now - before.Cpu).TotalSeconds) / seconds / cores * 100;
                    ioRate = io >= before.Io ? (io - before.Io) / seconds : 0;
                }

                if (!groups.TryGetValue(name, out var group))
                {
                    groups[name] = group = new Group();
                }

                group.Count++;
                group.Cpu += cpu;
                group.Memory += memory;
                group.Io += ioRate;
                if (gpuByProcess.TryGetValue(id, out var gpu))
                {
                    group.Gpu = Math.Max(group.Gpu ?? 0, gpu);
                }
            }
        }

        _previousProcesses = current;
        _unreadable = stillUnreadable; // forget programs that ended, so a reused number is asked again
        var anyGpu = gpuByProcess.Count > 0;
        return [.. groups.Select(g => new ProcessUsage(g.Key, g.Value.Count, Math.Clamp(g.Value.Cpu, 0, 100), g.Value.Memory, anyGpu ? g.Value.Gpu ?? 0 : null, g.Value.Io))];
    }

    /// <summary>
    /// Everything that needs to open the program: its start time (a process number can be reused, so it is part
    /// of the identity), processor time and I/O. False if Windows refuses, after only one failed attempt.
    /// </summary>
    private static bool TryReadHandleBased(Process process, out long key, out TimeSpan cpu, out ulong io)
    {
        key = 0;
        cpu = default;
        io = 0;
        try
        {
            key = HashCode.Combine(process.Id, process.StartTime.Ticks);
            cpu = process.TotalProcessorTime;
            io = ReadIo(process);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static unsafe ulong ReadIo(Process process)
    {
        try
        {
            using var handle = process.SafeHandle;
            IO_COUNTERS counters;
            return PInvoke.GetProcessIoCounters(new HANDLE(handle.DangerousGetHandle()), &counters)
                ? counters.ReadTransferCount + counters.WriteTransferCount
                : 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return 0;
        }
    }

    private sealed class Group
    {
        public int Count;
        public double Cpu;
        public long Memory;
        public double Io;
        public double? Gpu;
    }

    // ---- GPU ---------------------------------------------------------------------------------

    /// <summary>
    /// Per-program GPU use from the "GPU Engine" counters, whose instance names look like
    /// <c>pid_1234_luid_..._engtype_3D</c>. A program's figure is its busiest engine type, and the
    /// whole GPU's is the busiest engine type overall, which is what Task Manager shows.
    /// </summary>
    private Dictionary<int, double> ReadGpu(out double? total)
    {
        total = null;
        var perProcess = new Dictionary<int, double>();
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return perProcess;
            }

            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
            if (!data.Contains("Utilization Percentage"))
            {
                return perProcess;
            }

            var next = new Dictionary<string, CounterSample>();
            var byProcessAndType = new Dictionary<(int Pid, string Type), double>();
            var byType = new Dictionary<string, double>();

            foreach (InstanceData instance in data["Utilization Percentage"].Values)
            {
                next[instance.InstanceName] = instance.Sample;
                if (!_previousGpu.TryGetValue(instance.InstanceName, out var before) || !TryParseGpuInstance(instance.InstanceName, out var pid, out var type))
                {
                    continue;
                }

                var value = Math.Clamp(CounterSample.Calculate(before, instance.Sample), 0, 100);
                byProcessAndType[(pid, type)] = byProcessAndType.GetValueOrDefault((pid, type)) + value;
                byType[type] = byType.GetValueOrDefault(type) + value;
            }

            _previousGpu = next;
            foreach (var ((pid, _), value) in byProcessAndType)
            {
                perProcess[pid] = Math.Min(100, Math.Max(perProcess.GetValueOrDefault(pid), value));
            }

            total = byType.Count == 0 ? 0 : Math.Min(100, byType.Values.Max());
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            // No GPU counters here; the GPU figures are left out.
        }

        return perProcess;
    }

    internal static bool TryParseGpuInstance(string name, out int pid, out string type)
    {
        pid = 0;
        type = string.Empty;
        const string pidPrefix = "pid_";
        const string typeMarker = "_engtype_";

        if (!name.StartsWith(pidPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var underscore = name.IndexOf('_', pidPrefix.Length);
        var typeAt = name.LastIndexOf(typeMarker, StringComparison.Ordinal);
        if (underscore < 0 || typeAt < 0 || !int.TryParse(name.AsSpan(pidPrefix.Length, underscore - pidPrefix.Length), out pid))
        {
            return false;
        }

        type = name[(typeAt + typeMarker.Length)..];
        return type.Length > 0;
    }

    // ---- disk and network --------------------------------------------------------------------

    private (double? Percent, double Read, double Write) ReadDisk()
    {
        if (!_diskTried)
        {
            _diskTried = true;
            try
            {
                _diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
                _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", readOnly: true);
                _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                _diskIdle = _diskRead = _diskWrite = null;
            }
        }

        if (_diskIdle is null || _diskRead is null || _diskWrite is null)
        {
            return (null, 0, 0);
        }

        try
        {
            // Task Manager's "active time" is 100 minus the time the disk was idle.
            return (Math.Clamp(100 - _diskIdle.NextValue(), 0, 100), _diskRead.NextValue(), _diskWrite.NextValue());
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return (null, 0, 0);
        }
    }

    private (double Down, double Up) ReadNetwork(double seconds)
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
            return (0, 0);
        }

        var previous = _previousNetwork;
        _previousNetwork = (received, sent);
        if (previous is not { } p)
        {
            return (0, 0);
        }

        return (Math.Max(0, received - p.Received) / seconds, Math.Max(0, sent - p.Sent) / seconds);
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _diskIdle?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
    }
}
