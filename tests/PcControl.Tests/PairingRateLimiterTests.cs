using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Pairing;

namespace PcControl.Tests;

public class PairingRateLimiterTests
{
    private readonly FakeTimeProvider _time = new();

    private PairingRateLimiter Create() =>
        new(_time, maxFailures: 3, window: TimeSpan.FromMinutes(10), lockout: TimeSpan.FromMinutes(15));

    [Fact]
    public void NewClient_IsNotLocked()
    {
        Assert.False(Create().IsLocked("1.2.3.4", out _));
    }

    [Fact]
    public void LocksAfterMaxFailures()
    {
        var limiter = Create();

        limiter.RecordFailure("a");
        limiter.RecordFailure("a");
        Assert.False(limiter.IsLocked("a", out _));

        limiter.RecordFailure("a");
        Assert.True(limiter.IsLocked("a", out var retryAfter));
        Assert.Equal(TimeSpan.FromMinutes(15), retryAfter);
    }

    [Fact]
    public void LockoutEnds()
    {
        var limiter = Create();
        for (var i = 0; i < 3; i++)
        {
            limiter.RecordFailure("a");
        }

        _time.Advance(TimeSpan.FromMinutes(15));

        Assert.False(limiter.IsLocked("a", out _));
    }

    [Fact]
    public void FailuresOutsideWindowDoNotCount()
    {
        var limiter = Create();
        limiter.RecordFailure("a");
        limiter.RecordFailure("a");

        _time.Advance(TimeSpan.FromMinutes(11));
        limiter.RecordFailure("a");

        Assert.False(limiter.IsLocked("a", out _));
    }

    [Fact]
    public void ClientsAreIndependent()
    {
        var limiter = Create();
        for (var i = 0; i < 3; i++)
        {
            limiter.RecordFailure("a");
        }

        Assert.True(limiter.IsLocked("a", out _));
        Assert.False(limiter.IsLocked("b", out _));
    }

    [Fact]
    public void SuccessClearsFailures()
    {
        var limiter = Create();
        limiter.RecordFailure("a");
        limiter.RecordFailure("a");

        limiter.RecordSuccess("a");
        limiter.RecordFailure("a");
        limiter.RecordFailure("a");

        Assert.False(limiter.IsLocked("a", out _));
    }
}
