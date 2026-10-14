using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Pairing;

namespace PcControl.Tests;

public class PinServiceTests
{
    private readonly FakeTimeProvider _time = new();

    private PinService Create(int maxFailures = 3) =>
        new(_time, TimeSpan.FromMinutes(5), maxFailures);

    [Fact]
    public void Issue_ReturnsSixDigitPin()
    {
        var ticket = Create().Issue();

        Assert.Equal(6, ticket.Pin.Length);
        Assert.All(ticket.Pin, c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Fact]
    public void TryConsume_CorrectPin_SucceedsOnceOnly()
    {
        var service = Create();
        var pin = service.Issue().Pin;

        Assert.True(service.TryConsume(pin));
        Assert.False(service.TryConsume(pin));
        Assert.Null(service.Current);
    }

    [Fact]
    public void TryConsume_WrongPin_Fails()
    {
        var service = Create();
        var pin = service.Issue().Pin;
        var wrong = pin == "000000" ? "000001" : "000000";

        Assert.False(service.TryConsume(wrong));
        Assert.True(service.TryConsume(pin));
    }

    [Fact]
    public void TryConsume_NullOrEmpty_Fails()
    {
        var service = Create();
        service.Issue();

        Assert.False(service.TryConsume(null));
        Assert.False(service.TryConsume(string.Empty));
    }

    [Fact]
    public void TryConsume_NoPinIssued_Fails()
    {
        Assert.False(Create().TryConsume("123456"));
    }

    [Fact]
    public void Pin_ExpiresAfterLifetime()
    {
        var service = Create();
        var pin = service.Issue().Pin;

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(service.Current);
        Assert.False(service.TryConsume(pin));
    }

    [Fact]
    public void Pin_IsDiscardedAfterTooManyWrongGuesses()
    {
        var service = Create(maxFailures: 3);
        var pin = service.Issue().Pin;
        var wrong = pin == "000000" ? "000001" : "000000";

        service.TryConsume(wrong);
        service.TryConsume(wrong);
        service.TryConsume(wrong);

        Assert.False(service.TryConsume(pin));
        Assert.Null(service.Current);
    }

    [Fact]
    public void Issue_ReplacesPreviousPinAndResetsFailures()
    {
        var service = Create(maxFailures: 2);
        var first = service.Issue().Pin;
        service.TryConsume(first == "000000" ? "000001" : "000000");

        var second = service.Issue().Pin;

        Assert.True(service.TryConsume(second));
    }
}
