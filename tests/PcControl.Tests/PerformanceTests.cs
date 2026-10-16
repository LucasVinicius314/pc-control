using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PcControl.Core.Performance;
using PcControl.Tests.Integration;
using PcControl.Windows;
using Xunit.Abstractions;

namespace PcControl.Tests;

public sealed class FakePerformance : IPerformanceSource
{
    public int Reads { get; private set; }

    public PerformanceSnapshot Snapshot { get; set; } = new(
        CpuPercent: 37.5,
        Cores: 8,
        MemoryUsedBytes: 12L * 1024 * 1024 * 1024,
        MemoryTotalBytes: 32L * 1024 * 1024 * 1024,
        GpuPercent: 64,
        DiskPercent: 12,
        DiskReadBytesPerSecond: 1_000_000,
        DiskWriteBytesPerSecond: 500_000,
        NetworkDownBytesPerSecond: 2_000_000,
        NetworkUpBytesPerSecond: 100_000,
        Processes:
        [
            new ProcessUsage("chrome", 12, 20, 2_000_000_000, 5, 300_000),
            new ProcessUsage("game", 1, 12, 4_000_000_000, 60, 50_000),
            new ProcessUsage("backup", 1, 1, 100_000_000, null, 9_000_000),
            new ProcessUsage("notepad", 1, 0, 50_000_000, 0, 0),
        ]);

    public PerformanceSnapshot Read()
    {
        Reads++;
        return Snapshot;
    }
}

public class PerformanceServiceTests
{
    private readonly FakePerformance _source = new();
    private readonly FakeTimeProvider _time = new();

    private PerformanceService Create(TimeSpan? interval = null) => new(_source, _time, interval);

    [Theory]
    [InlineData(null, ProcessSort.Cpu)]
    [InlineData("", ProcessSort.Cpu)]
    [InlineData("cpu", ProcessSort.Cpu)]
    [InlineData("MEMORY", ProcessSort.Memory)]
    [InlineData("ram", ProcessSort.Memory)]
    [InlineData("gpu", ProcessSort.Gpu)]
    [InlineData("disk", ProcessSort.Disk)]
    [InlineData("io", ProcessSort.Disk)]
    public void SortNames_AreParsed(string? name, ProcessSort expected)
    {
        Assert.True(PerformanceService.TryParseSort(name, out var sort));
        Assert.Equal(expected, sort);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("cpu; drop")]
    public void UnknownSortNames_AreRefused(string name)
    {
        Assert.False(PerformanceService.TryParseSort(name, out _));
    }

    [Theory]
    [InlineData(ProcessSort.Cpu, new[] { "chrome", "game", "backup", "notepad" })]
    [InlineData(ProcessSort.Memory, new[] { "game", "chrome", "backup", "notepad" })]
    [InlineData(ProcessSort.Gpu, new[] { "game", "chrome", "notepad", "backup" })] // no GPU number sorts last
    [InlineData(ProcessSort.Disk, new[] { "backup", "chrome", "game", "notepad" })]
    public void TheBusiestProgramsComeFirst_ByTheChosenColumn(ProcessSort sort, string[] expected)
    {
        var result = Create().Get(sort, 10);

        Assert.Equal(expected, result.Processes.Select(p => p.Name));
    }

    [Fact]
    public void TheCount_IsLimited_AndNeverBelowOne()
    {
        var service = Create();

        Assert.Equal(2, service.Get(ProcessSort.Cpu, 2).Processes.Count);
        Assert.Single(service.Get(ProcessSort.Cpu, 0).Processes);
        Assert.Single(service.Get(ProcessSort.Cpu, -5).Processes);
    }

    [Fact]
    public void ManyAskersAtOnce_CauseOneMeasurement()
    {
        var service = Create(TimeSpan.FromSeconds(1));

        service.Get(ProcessSort.Cpu, 5);
        service.Get(ProcessSort.Memory, 5);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        service.Get(ProcessSort.Cpu, 5);

        Assert.Equal(1, _source.Reads);
    }

    [Fact]
    public void ALaterAsk_MeasuresAgain()
    {
        var service = Create(TimeSpan.FromSeconds(1));
        service.Get(ProcessSort.Cpu, 5);

        _time.Advance(TimeSpan.FromSeconds(1));
        service.Get(ProcessSort.Cpu, 5);

        Assert.Equal(2, _source.Reads);
    }

    [Fact]
    public void TheWholePcNumbers_AreNotChangedBySorting()
    {
        var result = Create().Get(ProcessSort.Disk, 1);

        Assert.Equal(37.5, result.CpuPercent);
        Assert.Equal(64, result.GpuPercent);
        Assert.Equal(8, result.Cores);
    }
}

public class PerformanceApiTests
{
    [Fact]
    public async Task ItNeedsPairing()
    {
        await using var server = await TestServer.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync("/api/performance")).StatusCode);
    }

    [Fact]
    public async Task ItGivesTheWholePc_AndTheBusiestPrograms()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var body = await server.Http.GetFromJsonAsync<JsonElement>("/api/performance?sort=memory&count=2");

        Assert.Equal(37.5, body.GetProperty("cpu").GetDouble());
        Assert.Equal(8, body.GetProperty("cores").GetInt32());
        Assert.Equal(32L * 1024 * 1024 * 1024, body.GetProperty("memory").GetProperty("total").GetInt64());
        Assert.Equal(64, body.GetProperty("gpu").GetDouble());
        Assert.Equal(2_000_000, body.GetProperty("network").GetProperty("downPerSecond").GetDouble());
        Assert.Equal(["game", "chrome"], body.GetProperty("processes").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task AMissingGpuFigure_IsNull()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var body = await server.Http.GetFromJsonAsync<JsonElement>("/api/performance?sort=disk");

        var backup = body.GetProperty("processes").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "backup");
        Assert.Equal(JsonValueKind.Null, backup.GetProperty("gpu").ValueKind);
    }

    [Fact]
    public async Task NoProcessNumbers_AreEverSent()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var text = await server.Http.GetStringAsync("/api/performance");

        Assert.DoesNotContain("pid", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownSort_IsRefused()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await server.Http.GetAsync("/api/performance?sort=bogus")).StatusCode);
    }

    [Fact]
    public async Task TheCapability_IsOn()
    {
        await using var server = await TestServer.StartAsync();
        await server.PairAsync();

        var caps = await server.Http.GetFromJsonAsync<Dictionary<string, bool>>("/api/capabilities");

        Assert.True(caps!["performance"]);
    }
}

public class PerformanceSamplerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000F3A1_phys_0_eng_0_engtype_3D", true, 1234, "3D")]
    [InlineData("pid_7_luid_0x0_0x1_phys_0_eng_5_engtype_VideoDecode", true, 7, "VideoDecode")]
    [InlineData("luid_0x0_0x1_phys_0_eng_0_engtype_3D", false, 0, "")]
    [InlineData("pid_abc_luid_x_engtype_3D", false, 0, "")]
    [InlineData("pid_5_luid_x", false, 0, "")]
    [InlineData("", false, 0, "")]
    public void GpuInstanceNames_AreParsed(string name, bool ok, int pid, string type)
    {
        var parsed = PerformanceSampler.TryParseGpuInstance(name, out var gotPid, out var gotType);

        Assert.Equal(ok, parsed);
        if (ok)
        {
            Assert.Equal(pid, gotPid);
            Assert.Equal(type, gotType);
        }
    }

    [Fact]
    public void TheRealNumbers_AreSensible()
    {
        if (Environment.GetEnvironmentVariable("PCCONTROL_REAL_INPUT") != "1")
        {
            return;
        }

        using var sampler = new PerformanceSampler();
        var first = sampler.Read();
        Thread.Sleep(1200);
        var s = sampler.Read();

        output.WriteLine($"CPU {s.CpuPercent:F1}% of {s.Cores} cores | RAM {s.MemoryUsedBytes / 1e9:F1} / {s.MemoryTotalBytes / 1e9:F1} GB | GPU {s.GpuPercent?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}% | disk {s.DiskPercent?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}% r {s.DiskReadBytesPerSecond / 1e6:F2} w {s.DiskWriteBytesPerSecond / 1e6:F2} MB/s | net down {s.NetworkDownBytesPerSecond / 1e6:F3} up {s.NetworkUpBytesPerSecond / 1e6:F3} MB/s | {s.Processes.Count} programs");
        foreach (var p in PerformanceService.Top(s, ProcessSort.Cpu, 6).Processes)
        {
            output.WriteLine($"  {p.Name,-28} x{p.Count,-3} cpu {p.CpuPercent,5:F1}%  ram {p.MemoryBytes / 1e6,7:F0} MB  gpu {p.GpuPercent?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a",5}  io {p.IoBytesPerSecond / 1e6:F2} MB/s");
        }

        Assert.InRange(s.CpuPercent, 0, 100);
        Assert.True(s.MemoryTotalBytes > 0 && s.MemoryUsedBytes > 0 && s.MemoryUsedBytes <= s.MemoryTotalBytes);
        Assert.NotEmpty(s.Processes);
        Assert.True(first.Processes.Count > 0);
    }
}
