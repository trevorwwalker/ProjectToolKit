namespace Scheduler.Core.Contracts;

/// <summary>One sample of the host's load (ACCT-9).</summary>
public sealed record HostLoadSample
{
    /// <summary>CPU use in percent.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Free memory in bytes.</summary>
    public long FreeMemoryBytes { get; init; }

    /// <summary>Total memory in bytes.</summary>
    public long TotalMemoryBytes { get; init; }

    /// <summary>Logical processors, for threads per worker (WRK-7).</summary>
    public int Cores { get; init; } = 1;
}
