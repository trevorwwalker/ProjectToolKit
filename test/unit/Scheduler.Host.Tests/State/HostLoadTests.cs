using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Host.State;

namespace Scheduler.Host.Tests.State;

/// <summary>
/// The host load taken at the top of every tick (ACCT-9, <c>Sample-Host</c> L963): CPU percent,
/// free and total memory, and the threads each worker gets. Test plan: accounting.md.
/// </summary>
public class HostLoadTests
{
    // ACCT-9: each tick has CPU %, free and total memory and threads per worker, all numeric and in range.
    [Fact]
    public void Acct_HostLoad_Sampled()
    {
        var host = new HostLoad();
        var first = host.Sample();
        Spin(TimeSpan.FromMilliseconds(200));
        var sample = host.Sample();

        Assert.InRange(first.CpuPercent, 0d, 100d);
        Assert.InRange(sample.CpuPercent, 0d, 100d);
        Assert.Equal(Environment.ProcessorCount, sample.Cores);
        Assert.True(sample.TotalMemoryBytes > 0, $"total memory {sample.TotalMemoryBytes}");
        Assert.True(
            sample.FreeMemoryBytes > 0 && sample.FreeMemoryBytes <= sample.TotalMemoryBytes,
            $"free {sample.FreeMemoryBytes} of {sample.TotalMemoryBytes}");

        var text = HostLoad.TextOf(sample, slots: 3);
        var parts = Regex.Match(text, @"^CPU (\d+) %, memory free ([0-9.]+) of ([0-9.]+) GB, (\d+) threads per worker$");
        Assert.True(parts.Success, text);
        Assert.Equal(HostLoad.ShownCpuPercent(sample), int.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture));
        var free = double.Parse(parts.Groups[2].Value, CultureInfo.InvariantCulture);
        var total = double.Parse(parts.Groups[3].Value, CultureInfo.InvariantCulture);
        Assert.True(free > 0 && total >= free, text);
        Assert.Equal(
            WorkerSelection.JobsPerWorker(sample.Cores, 3),
            int.Parse(parts.Groups[4].Value, CultureInfo.InvariantCulture));

        // Threads per worker is at least one however many slots there are.
        Assert.EndsWith(", 1 threads per worker", HostLoad.TextOf(sample, slots: sample.Cores * 4), StringComparison.Ordinal);
    }

    // ACCT-9 (1094-B): the processor-utility reading is capped at 100 and used when present;
    // when the counter has no value (null, NaN, negative) the system-times value stands.
    [Theory]
    [InlineData(42.5, 7.0, 42.5)]
    [InlineData(110.2, 7.0, 100.0)]
    [InlineData(0.0, 7.0, 0.0)]
    [InlineData(null, 7.0, 7.0)]
    [InlineData(double.NaN, 7.0, 7.0)]
    [InlineData(-1.0, 7.0, 7.0)]
    public void Acct_HostLoad_UtilityChosenOrFallback(double? utility, double fallback, double expected)
    {
        Assert.Equal(expected, HostLoad.ChooseCpu(utility, fallback));
    }

    // ACCT-9 (1094-B): Sample shows the reader's value capped at 100, and falls back to system times without one.
    [Fact]
    public void Acct_HostLoad_SampleUsesUtilityCappedAndFallsBack()
    {
        Assert.Equal(100d, new HostLoad(() => 130d).Sample().CpuPercent);
        Assert.Equal(55.5, new HostLoad(() => 55.5).Sample().CpuPercent);
        var fallback = new HostLoad(() => null);
        Spin(TimeSpan.FromMilliseconds(100));
        Assert.InRange(fallback.Sample().CpuPercent, 0d, 100d);
    }

    // ACCT-9 (710-B): the CPU number the line shows is HostLoad.ShownCpuPercent's own rounding, so a
    // reading on the .5 boundary (80.5, 81.5) says the same number in the text and in the helper.
    [Theory]
    [InlineData(80.5, 81)]
    [InlineData(81.5, 82)]
    [InlineData(80.4, 80)]
    [InlineData(79.5, 80)]
    [InlineData(0.5, 1)]
    [InlineData(99.5, 100)]
    public void Acct_HostLoad_CpuPercentShownOneWay(double cpuPercent, int expected)
    {
        var sample = new HostLoadSample
        {
            CpuPercent = cpuPercent,
            FreeMemoryBytes = 6L * 1024 * 1024 * 1024,
            TotalMemoryBytes = 32L * 1024 * 1024 * 1024,
            Cores = 8,
        };

        Assert.Equal(expected, HostLoad.ShownCpuPercent(sample));

        var parts = Regex.Match(
            HostLoad.TextOf(sample, slots: 3),
            @"^CPU (\d+) %, memory free ([0-9.]+) of ([0-9.]+) GB, (\d+) threads per worker$");
        Assert.True(parts.Success, HostLoad.TextOf(sample, slots: 3));
        Assert.Equal(
            HostLoad.ShownCpuPercent(sample),
            int.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private static void Spin(TimeSpan forHowLong)
    {
        var watch = Stopwatch.StartNew();
        var sink = 0d;
        while (watch.Elapsed < forHowLong)
        {
            sink += Math.Sqrt(sink + 1);
        }

        Assert.True(sink > 0);
    }
}
