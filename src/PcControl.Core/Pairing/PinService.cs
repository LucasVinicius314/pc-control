using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PcControl.Core.Pairing;

public sealed record PinTicket(string Pin, DateTimeOffset ExpiresAt);

/// <summary>
/// Holds the single active 6-digit pairing PIN. A PIN is single use, expires after a few minutes
/// and is discarded after too many wrong guesses.
/// </summary>
public sealed class PinService
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly int _maxFailures;
    private readonly object _gate = new();

    private PinTicket? _current;
    private int _failures;

    public PinService(TimeProvider time, TimeSpan lifetime, int maxFailures)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFailures, 1);

        _time = time;
        _lifetime = lifetime;
        _maxFailures = maxFailures;
    }

    /// <summary>Creates a fresh PIN, replacing any previous one.</summary>
    public PinTicket Issue()
    {
        var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var ticket = new PinTicket(pin, _time.GetUtcNow() + _lifetime);

        lock (_gate)
        {
            _current = ticket;
            _failures = 0;
        }

        return ticket;
    }

    /// <summary>The active PIN, or null when none was issued, it expired or it was used.</summary>
    public PinTicket? Current
    {
        get
        {
            lock (_gate)
            {
                DropIfExpired();
                return _current;
            }
        }
    }

    /// <summary>Checks a PIN guess. A correct guess consumes the PIN.</summary>
    public bool TryConsume(string? candidate)
    {
        lock (_gate)
        {
            DropIfExpired();

            if (_current is null)
            {
                return false;
            }

            if (candidate is not null && FixedTimeEquals(candidate, _current.Pin))
            {
                _current = null;
                _failures = 0;
                return true;
            }

            _failures++;
            if (_failures >= _maxFailures)
            {
                _current = null;
                _failures = 0;
            }

            return false;
        }
    }

    private void DropIfExpired()
    {
        if (_current is not null && _time.GetUtcNow() >= _current.ExpiresAt)
        {
            _current = null;
            _failures = 0;
        }
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
