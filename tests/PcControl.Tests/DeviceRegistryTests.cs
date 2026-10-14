using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Pairing;

namespace PcControl.Tests;

public class DeviceRegistryTests
{
    private sealed class MemoryStore : IDeviceStore
    {
        public List<PairedDevice> Saved { get; private set; } = [];

        public IReadOnlyList<PairedDevice> Load() => Saved;

        public void Save(IReadOnlyList<PairedDevice> devices) => Saved = [.. devices];
    }

    private readonly MemoryStore _store = new();
    private readonly FakeTimeProvider _time = new();

    private DeviceRegistry Create() => new(_store, _time);

    [Fact]
    public void Pair_ReturnsTokenThatAuthenticates()
    {
        var registry = Create();

        var result = registry.Pair("My phone");

        Assert.Equal(43, result.Token.Length);
        var found = registry.Authenticate(result.Token);
        Assert.NotNull(found);
        Assert.Equal(result.Device.Id, found.Id);
        Assert.Equal("My phone", found.Name);
    }

    [Fact]
    public void Pair_StoresOnlyTheHash()
    {
        var registry = Create();

        var result = registry.Pair("p");

        var saved = Assert.Single(_store.Saved);
        Assert.NotEqual(result.Token, saved.TokenHash);
        Assert.DoesNotContain(result.Token, saved.TokenHash, StringComparison.Ordinal);
        Assert.Equal(DeviceRegistry.HashToken(result.Token), saved.TokenHash);
    }

    [Fact]
    public void Tokens_AreUnique()
    {
        var registry = Create();

        Assert.NotEqual(registry.Pair("a").Token, registry.Pair("b").Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890123456789012")]
    public void Authenticate_UnknownToken_ReturnsNull(string? token)
    {
        var registry = Create();
        registry.Pair("p");

        Assert.Null(registry.Authenticate(token));
    }

    [Fact]
    public void Revoke_InvalidatesToken()
    {
        var registry = Create();
        var result = registry.Pair("p");

        Assert.True(registry.Revoke(result.Device.Id));

        Assert.Null(registry.Authenticate(result.Token));
        Assert.Empty(_store.Saved);
        Assert.False(registry.Revoke(result.Device.Id));
    }

    [Fact]
    public void Registry_LoadsExistingDevices()
    {
        var first = Create();
        var result = first.Pair("p");

        var second = Create();

        Assert.NotNull(second.Authenticate(result.Token));
    }

    [Fact]
    public void Events_AreRaised()
    {
        var registry = Create();
        PairedDevice? paired = null;
        PairedDevice? revoked = null;
        registry.DevicePaired += (_, d) => paired = d;
        registry.DeviceRevoked += (_, d) => revoked = d;

        var result = registry.Pair("p");
        registry.Revoke(result.Device.Id);

        Assert.Equal(result.Device, paired);
        Assert.Equal(result.Device, revoked);
    }

    [Theory]
    [InlineData(null, "Phone")]
    [InlineData("", "Phone")]
    [InlineData("   ", "Phone")]
    [InlineData("\n\t", "Phone")]
    [InlineData("  Pixel 8 ", "Pixel 8")]
    [InlineData("Pi\nxel", "Pixel")]
    public void SanitizeName_CleansInput(string? input, string expected)
    {
        Assert.Equal(expected, DeviceRegistry.SanitizeName(input));
    }

    [Fact]
    public void SanitizeName_TruncatesLongNames()
    {
        var name = DeviceRegistry.SanitizeName(new string('x', 500));

        Assert.Equal(DeviceRegistry.MaxNameLength, name.Length);
    }
}
