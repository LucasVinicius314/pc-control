namespace PcControl.Core.Performance;

/// <summary>What one program (all of its processes together) is using right now.</summary>
/// <param name="Name">The program's name, for example "chrome". Never a process id.</param>
/// <param name="Count">How many processes of that name are running.</param>
/// <param name="CpuPercent">Share of the whole CPU, 0 to 100.</param>
/// <param name="MemoryBytes">Working set: the memory it holds in RAM.</param>
/// <param name="GpuPercent">Busiest GPU engine's use, or null when Windows gives no GPU numbers.</param>
/// <param name="IoBytesPerSecond">Data read and written (files, and network too; Windows counts them together per program).</param>
public sealed record ProcessUsage(string Name, int Count, double CpuPercent, long MemoryBytes, double? GpuPercent, double IoBytesPerSecond);

/// <summary>How busy the whole PC is, measured over the time since the previous reading.</summary>
public sealed record PerformanceSnapshot(
    double CpuPercent,
    int Cores,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double? GpuPercent,
    double? DiskPercent,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond,
    double NetworkDownBytesPerSecond,
    double NetworkUpBytesPerSecond,
    IReadOnlyList<ProcessUsage> Processes);

public interface IPerformanceSource
{
    /// <summary>Reads the numbers. Each call reports the use since the call before it.</summary>
    PerformanceSnapshot Read();
}

public enum ProcessSort
{
    Cpu,
    Memory,
    Gpu,
    Disk,
}

/// <summary>
/// Task-manager style numbers for the phone and the controller UI. Readings are shared: many screens
/// asking at once cause one measurement a second, not one each.
/// </summary>
public sealed class PerformanceService(IPerformanceSource source, TimeProvider time, TimeSpan? minimumInterval = null)
{
    public const int MaxProcesses = 1000;

    private readonly TimeSpan _minimumInterval = minimumInterval ?? TimeSpan.FromMilliseconds(900);
    private readonly object _gate = new();
    private PerformanceSnapshot? _last;
    private DateTimeOffset _lastAt;

    public static bool TryParseSort(string? name, out ProcessSort sort)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "cpu":
                sort = ProcessSort.Cpu;
                return true;
            case "memory":
            case "ram":
                sort = ProcessSort.Memory;
                return true;
            case "gpu":
                sort = ProcessSort.Gpu;
                return true;
            case "disk":
            case "io":
                sort = ProcessSort.Disk;
                return true;
            default:
                sort = default;
                return false;
        }
    }

    /// <summary>The latest numbers with the busiest programs first (by <paramref name="sort"/>), at most <paramref name="count"/>.</summary>
    public PerformanceSnapshot Get(ProcessSort sort, int count)
    {
        PerformanceSnapshot snapshot;
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_last is null || now - _lastAt >= _minimumInterval)
            {
                _last = source.Read();
                _lastAt = now;
            }

            snapshot = _last;
        }

        return Top(snapshot, sort, count);
    }

    public static PerformanceSnapshot Top(PerformanceSnapshot snapshot, ProcessSort sort, int count)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var limit = Math.Clamp(count, 1, MaxProcesses);
        IOrderedEnumerable<ProcessUsage> ordered = sort switch
        {
            ProcessSort.Memory => snapshot.Processes.OrderByDescending(p => p.MemoryBytes),
            ProcessSort.Gpu => snapshot.Processes.OrderByDescending(p => p.GpuPercent ?? -1),
            ProcessSort.Disk => snapshot.Processes.OrderByDescending(p => p.IoBytesPerSecond),
            _ => snapshot.Processes.OrderByDescending(p => p.CpuPercent),
        };

        return snapshot with { Processes = [.. ordered.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(limit)] };
    }
}
