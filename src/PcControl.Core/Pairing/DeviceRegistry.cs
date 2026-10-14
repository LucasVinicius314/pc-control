using System.Security.Cryptography;
using System.Text;

namespace PcControl.Core.Pairing;

/// <summary>A paired phone. Only a hash of its token is kept on the PC.</summary>
public sealed record PairedDevice(Guid Id, string Name, string TokenHash, DateTimeOffset PairedAt);

public sealed record PairingResult(PairedDevice Device, string Token);

public interface IDeviceStore
{
    IReadOnlyList<PairedDevice> Load();

    void Save(IReadOnlyList<PairedDevice> devices);
}

/// <summary>Issues, validates and revokes device tokens.</summary>
public sealed class DeviceRegistry
{
    public const int TokenBytes = 32;
    public const int MaxNameLength = 64;
    public const string DefaultName = "Phone";

    // base64url of 32 bytes is always 43 characters.
    private const int TokenLength = 43;

    private readonly IDeviceStore _store;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly List<PairedDevice> _devices;

    public DeviceRegistry(IDeviceStore store, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);

        _store = store;
        _time = time;
        _devices = [.. store.Load()];
    }

    public event EventHandler<PairedDevice>? DevicePaired;

    public event EventHandler<PairedDevice>? DeviceRevoked;

    public IReadOnlyList<PairedDevice> List()
    {
        lock (_gate)
        {
            return [.. _devices];
        }
    }

    public PairingResult Pair(string? requestedName)
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        var device = new PairedDevice(Guid.NewGuid(), SanitizeName(requestedName), HashToken(token), _time.GetUtcNow());

        lock (_gate)
        {
            _devices.Add(device);
            _store.Save(_devices);
        }

        DevicePaired?.Invoke(this, device);
        return new PairingResult(device, token);
    }

    /// <summary>Returns the device that owns <paramref name="token"/>, or null if it is unknown.</summary>
    public PairedDevice? Authenticate(string? token)
    {
        if (token is null || token.Length != TokenLength)
        {
            return null;
        }

        // Tokens are 256 random bits, so matching on their hash leaks nothing useful through timing.
        var hash = HashToken(token);
        lock (_gate)
        {
            return _devices.Find(d => string.Equals(d.TokenHash, hash, StringComparison.Ordinal));
        }
    }

    public bool Revoke(Guid deviceId)
    {
        PairedDevice? removed;
        lock (_gate)
        {
            removed = _devices.Find(d => d.Id == deviceId);
            if (removed is null)
            {
                return false;
            }

            _devices.Remove(removed);
            _store.Save(_devices);
        }

        DeviceRevoked?.Invoke(this, removed);
        return true;
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return DefaultName;
        }

        var cleaned = new string([.. name.Where(c => !char.IsControl(c))]).Trim();
        if (cleaned.Length == 0)
        {
            return DefaultName;
        }

        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
