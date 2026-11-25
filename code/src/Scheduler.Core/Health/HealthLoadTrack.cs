// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;

namespace Scheduler.Core.Health;

/// <summary>1308-B: how loaded the PC was at one instant of a health check.</summary>
/// <param name="CpuPercent">The machine's CPU use in percent.</param>
/// <param name="FreeMemoryBytes">The machine's free memory in bytes.</param>
/// <param name="Workers">The workers running at that instant (the whole pool's, in a pool).</param>
public readonly record struct HealthLoadReading(double CpuPercent, long FreeMemoryBytes, int Workers);

/// <summary>
/// 1308-B: the load seen while one health check ran: the highest CPU, the lowest free memory and the most
/// workers at once, so one quiet instant at the end does not hide the load. Its text ends every result line.
/// </summary>
public sealed class HealthLoadTrack
{
    private bool _any;
    private double _cpu;
    private long _free;
    private int _workers;

    /// <summary>Takes one reading into the extremes; null (unreadable) changes nothing.</summary>
    /// <param name="reading">What was read, or null.</param>
    public void Add(HealthLoadReading? reading)
    {
        if (reading is not { } r)
        {
            return;
        }

        if (!_any)
        {
            (_any, _cpu, _free, _workers) = (true, r.CpuPercent, r.FreeMemoryBytes, r.Workers);
            return;
        }

        _cpu = Math.Max(_cpu, r.CpuPercent);
        _free = Math.Min(_free, r.FreeMemoryBytes);
        _workers = Math.Max(_workers, r.Workers);
    }

    /// <summary>The line's ending: <c>; load: CPU 71 %, 4.2 GB free, 3 workers</c>, or <c>; load: not known</c>.</summary>
    public string Text => _any
        ? string.Format(
            CultureInfo.InvariantCulture,
            "; load: CPU {0:0} %, {1:0.0} GB free, {2} workers",
            _cpu,
            _free / 1073741824.0,
            _workers)
        : "; load: not known";
}
