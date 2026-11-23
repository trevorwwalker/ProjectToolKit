using System.Globalization;
using System.Runtime.InteropServices;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Host.State;

/// <summary>
/// The host's CPU and memory, sampled at the top of every tick (ACCT-9, <c>Sample-Host</c> L963).
/// The percent is the work between two samples, so, as the script did, the sampler is primed once
/// when it is built and each tick's <see cref="Sample"/> is what that tick shows.
/// </summary>
public sealed class HostLoad : IHostLoad
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    private ulong _idle;
    private ulong _busy;
    private readonly Func<double?> _utility;
    private readonly IDisposable? _utilityOwner;

    /// <summary>
    /// Builds the sampler and primes the CPU counters, as <c>Sample-Host</c> does at start-up. The CPU
    /// is Windows' "% Processor Utility" (what Task Manager shows), with the system-times value as the fallback.
    /// </summary>
    public HostLoad()
        : this(OpenUtility(out var owner))
    {
        _utilityOwner = owner;
    }

    /// <summary>
    /// Builds a sampler whose CPU comes from <paramref name="utilityReader"/> (percent, may exceed 100,
    /// or null when unavailable, in which case the system-times value is used). For tests and the PDH path.
    /// </summary>
    public HostLoad(Func<double?>? utilityReader)
    {
        _utility = utilityReader ?? (() => null);
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

        cpu = ChooseCpu(_utility(), cpu);

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

    /// <summary>
    /// The CPU percent to show: the processor-utility reading capped at 100 when there is one (finite and
    /// not negative), otherwise the system-times <paramref name="fallback"/>.
    /// </summary>
    public static double ChooseCpu(double? utility, double fallback) =>
        utility is { } u && double.IsFinite(u) && u >= 0 ? Math.Min(u, 100d) : fallback;

    /// <summary>Opens the PDH counter and primes it; null reader when the counter is not available.</summary>
    private static Func<double?>? OpenUtility(out IDisposable? owner)
    {
        owner = null;
        try
        {
            var counter = PdhUtility.TryOpen();
            if (counter is null)
            {
                return null;
            }

            owner = counter;
            return counter.Read;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Idle and busy ticks, the kernel time counting idle once.</summary>
    private static (ulong Idle, ulong Busy) Busy(long idle, long kernel, long user) =>
        ((ulong)Math.Max(0, idle), (ulong)Math.Max(0, (kernel - idle) + user));

    /// <summary>The <c>\Processor Information(_Total)\% Processor Utility</c> counter through PDH.</summary>
    private sealed class PdhUtility : IDisposable
    {
        private const string Path = @"\Processor Information(_Total)\% Processor Utility";
        private const uint FormatDouble = 0x00000200;
        private readonly nint _query;
        private readonly nint _counter;

        private PdhUtility(nint query, nint counter)
        {
            _query = query;
            _counter = counter;
        }

        public static PdhUtility? TryOpen()
        {
            if (NativeMethods.PdhOpenQuery(null, 0, out var query) != 0)
            {
                return null;
            }

            if (NativeMethods.PdhAddEnglishCounterW(query, Path, 0, out var counter) != 0
                || NativeMethods.PdhCollectQueryData(query) != 0)
            {
                NativeMethods.PdhCloseQuery(query);
                return null;
            }

            return new PdhUtility(query, counter);
        }

        /// <summary>The value since the last read, or null when PDH has none.</summary>
        public double? Read()
        {
            if (NativeMethods.PdhCollectQueryData(_query) != 0
                || NativeMethods.PdhGetFormattedCounterValue(_counter, FormatDouble, out _, out var value) != 0
                || value.Status != 0)
            {
                return null;
            }

            return value.Value;
        }

        public void Dispose() => NativeMethods.PdhCloseQuery(_query);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Explicit)]
        public struct PdhFmtCounterValue
        {
            [FieldOffset(0)]
            public uint Status;

            [FieldOffset(8)]
            public double Value;
        }

        [DllImport("pdh.dll")]
        public static extern uint PdhOpenQuery(string? source, nint userData, out nint query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

        [DllImport("pdh.dll")]
        public static extern uint PdhCollectQueryData(nint query);

        [DllImport("pdh.dll")]
        public static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PdhFmtCounterValue value);

        [DllImport("pdh.dll")]
        public static extern uint PdhCloseQuery(nint query);

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
