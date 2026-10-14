namespace PcControl.Core.Pairing;

/// <summary>Per-client lockout after repeated failed pairing attempts.</summary>
public sealed class PairingRateLimiter
{
    private readonly TimeProvider _time;
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockout;
    private readonly Dictionary<string, ClientState> _clients = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public PairingRateLimiter(TimeProvider time, int maxFailures, TimeSpan window, TimeSpan lockout)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFailures, 1);

        _time = time;
        _maxFailures = maxFailures;
        _window = window;
        _lockout = lockout;
    }

    /// <summary>True when the client is locked out; <paramref name="retryAfter"/> says for how long.</summary>
    public bool IsLocked(string clientKey, out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_clients.TryGetValue(clientKey, out var state) && state.LockedUntil is { } until && until > now)
            {
                retryAfter = until - now;
                return true;
            }

            retryAfter = TimeSpan.Zero;
            return false;
        }
    }

    public void RecordFailure(string clientKey)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Prune(now);

            if (!_clients.TryGetValue(clientKey, out var state))
            {
                state = new ClientState();
                _clients[clientKey] = state;
            }

            state.Failures.RemoveAll(t => now - t > _window);
            state.Failures.Add(now);

            if (state.Failures.Count >= _maxFailures)
            {
                state.LockedUntil = now + _lockout;
                state.Failures.Clear();
            }
        }
    }

    public void RecordSuccess(string clientKey)
    {
        lock (_gate)
        {
            _clients.Remove(clientKey);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var stale = _clients
            .Where(kv => (kv.Value.LockedUntil is null || kv.Value.LockedUntil <= now)
                         && kv.Value.Failures.TrueForAll(t => now - t > _window))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in stale)
        {
            _clients.Remove(key);
        }
    }

    private sealed class ClientState
    {
        public List<DateTimeOffset> Failures { get; } = [];

        public DateTimeOffset? LockedUntil { get; set; }
    }
}
