namespace PcControl.Core.Actions;

public enum PowerAction
{
    Lock,
    Sleep,
    Restart,
    Shutdown,
    SignOut,
}

/// <summary>Carries out power actions on the PC.</summary>
public interface IPowerBackend
{
    /// <summary>Starts the action; <paramref name="delaySeconds"/> is only used by restart and shutdown.</summary>
    bool Perform(PowerAction action, int delaySeconds);

    /// <summary>Cancels a restart or shutdown that is counting down.</summary>
    bool CancelScheduled();
}

public enum PowerOutcome
{
    Ok,
    ConfirmationRequired,
    Failed,
}

public sealed record PowerResult(PowerOutcome Outcome, PowerAction Action, int DelaySeconds);

public sealed record PendingPower(PowerAction Action, int SecondsLeft);

/// <summary>
/// Lock, sleep, restart, shut down and sign out. The actions that end the session need an explicit
/// confirmation, and restart and shutdown count down first so a wrong tap can be cancelled.
/// </summary>
public sealed class PowerService(IPowerBackend backend, TimeProvider time, int delaySeconds = PowerService.DefaultDelaySeconds)
{
    public const int DefaultDelaySeconds = 15;

    private readonly object _gate = new();
    private (PowerAction Action, DateTimeOffset At)? _pending;

    public static bool TryParseAction(string? name, out PowerAction action)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "lock":
                action = PowerAction.Lock;
                return true;
            case "sleep":
                action = PowerAction.Sleep;
                return true;
            case "restart":
                action = PowerAction.Restart;
                return true;
            case "shutdown":
                action = PowerAction.Shutdown;
                return true;
            case "signout":
                action = PowerAction.SignOut;
                return true;
            default:
                action = default;
                return false;
        }
    }

    public static bool NeedsConfirmation(PowerAction action) =>
        action is PowerAction.Restart or PowerAction.Shutdown or PowerAction.SignOut;

    public PowerResult Request(PowerAction action, bool confirmed)
    {
        if (NeedsConfirmation(action) && !confirmed)
        {
            return new PowerResult(PowerOutcome.ConfirmationRequired, action, 0);
        }

        var delay = action is PowerAction.Restart or PowerAction.Shutdown ? delaySeconds : 0;
        lock (_gate)
        {
            if (!backend.Perform(action, delay))
            {
                return new PowerResult(PowerOutcome.Failed, action, 0);
            }

            _pending = delay > 0 ? (action, time.GetUtcNow().AddSeconds(delay)) : null;
        }

        return new PowerResult(PowerOutcome.Ok, action, delay);
    }

    /// <summary>The restart or shutdown that is counting down, if any.</summary>
    public PendingPower? Pending
    {
        get
        {
            lock (_gate)
            {
                return PendingLocked();
            }
        }
    }

    /// <summary>Stops the countdown. False when nothing was counting down.</summary>
    public bool Cancel()
    {
        lock (_gate)
        {
            if (PendingLocked() is null)
            {
                return false;
            }

            var cancelled = backend.CancelScheduled();
            if (cancelled)
            {
                _pending = null;
            }

            return cancelled;
        }
    }

    private PendingPower? PendingLocked()
    {
        if (_pending is not { } pending)
        {
            return null;
        }

        var left = (int)Math.Ceiling((pending.At - time.GetUtcNow()).TotalSeconds);
        if (left <= 0)
        {
            _pending = null;
            return null;
        }

        return new PendingPower(pending.Action, left);
    }
}
