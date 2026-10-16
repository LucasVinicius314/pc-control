using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PcControl.Host.Web;

/// <summary>
/// Lets the controller-UI window on this PC pair itself without a PIN. The app generates a random key
/// when it starts and hands it to its own window; the endpoint only accepts that key, and only from this
/// PC. Nothing on the network can use it.
/// </summary>
internal sealed class LocalPairing
{
    public const string DeviceName = "PC controller UI";

    public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Key);
        var given = Encoding.UTF8.GetBytes(candidate);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
    }

    /// <summary>True only for a connection that comes from this very PC.</summary>
    public static bool IsThisPc(IPAddress? address) =>
        address is not null && (IPAddress.IsLoopback(address) || (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));
}
