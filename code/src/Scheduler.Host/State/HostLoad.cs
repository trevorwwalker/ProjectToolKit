// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Runtime.InteropServices;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Host.State;

/// <summary>
/// The host's CPU and memory, sampled at the top of every tick (ACCT-9, <c>Sample-Host</c> L963).
/// The CPU percent is "% Processor Time" (what Task Manager shows since 2025): busy time over the
/// elapsed time across all logical processors, from <c>GetSystemTimes</c>. The percent is the work
/// between two samples, so, as the script did, the sampler is primed once when it is built and each
/// tick's <see cref="Sample"/> is what that tick shows.
/// </summary>
public sealed class HostLoad : IHostLoad
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    private ulong _idle;
    private ulong _busy;

    /// <summary>
    /// Builds the sampler and primes the CPU counters, as <c>Sample-Host</c> does at start-up.
    /// </summary>
    public HostLoad()
    {
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            (_idle, _busy) = Busy(idle, kernel, user);
        }
    }

    /// <inheritdoc />
    public HostLoadSample Sample()
    {
        var cpu = 0d;
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var (nowIdle, nowBusy) = Busy(idle, kernel, user);
            var total = (nowIdle - _idle) + (nowBusy - _busy);
            cpu = total == 0 ? 0 : ((double)(nowBusy - _busy) / total) * 100;
            (_idle, _busy) = (nowIdle, nowBusy);
        }

        var memory = new NativeMethods.MemoryStatusEx();
        var free = 0L;
        var totalBytes = 0L;
        if (NativeMethods.GlobalMemoryStatusEx(ref memory))
        {
            free = (long)memory.AvailablePhysical;
            totalBytes = (long)memory.TotalPhysical;
        }

        return new HostLoadSample
        {
            CpuPercent = Math.Clamp(Math.Round(cpu, 1, MidpointRounding.AwayFromZero), 0d, 100d),
            FreeMemoryBytes = free,
            TotalMemoryBytes = totalBytes,
            Cores = Environment.ProcessorCount,
        };
    }

    /// <summary>
    /// The line the tick leaves behind (ACCT-9): <c>CPU n %, memory free a of b GB, t threads per
    /// worker</c>, the threads being WRK-7's share of the cores.
    /// </summary>
    public static string TextOf(HostLoadSample sample, int slots) => string.Format(
        CultureInfo.InvariantCulture,
        "CPU {0} %, memory free {1} of {2} GB, {3} threads per worker",
        ShownCpuPercent(sample),
        Math.Round(sample.FreeMemoryBytes / BytesPerGb, 1, MidpointRounding.AwayFromZero),
        Math.Round(sample.TotalMemoryBytes / BytesPerGb, 1, MidpointRounding.AwayFromZero),
        WorkerSelection.JobsPerWorker(sample.Cores, slots));

    /// <summary>
    /// The one rounding of the CPU percent every display uses (ACCT-9, 710-B): whole percent, half
    /// away from zero and clamped to 0-100, so a reading on the .5 boundary says the same number in
    /// <see cref="TextOf"/> and wherever else it is shown. <c>Math.Round</c> alone rounds half to
    /// even, which disagreed with the format string and made <c>Acct_HostLoad_Sampled</c> flake.
    /// </summary>
    public static int ShownCpuPercent(HostLoadSample sample) =>
        (int)Math.Clamp(Math.Round(sample.CpuPercent, MidpointRounding.AwayFromZero), 0d, 100d);

    /// <summary>Idle and busy ticks, the kernel time counting idle once.</summary>
    private static (ulong Idle, ulong Busy) Busy(long idle, long kernel, long user) =>
        ((ulong)Math.Max(0, idle), (ulong)Math.Max(0, (kernel - idle) + user));

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        /// <summary>MEMORYSTATUSEX, as GlobalMemoryStatusEx wants it (all ten fields).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryStatusEx
        {
            public uint Length;
            public uint Load;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;

            /// <summary>Sets the size field the API checks before it answers.</summary>
            public MemoryStatusEx()
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            }
        }
    }
}
