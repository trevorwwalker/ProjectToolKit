// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
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

    // ACCT-9 (1261-B): the CPU percent is the % Processor Time from GetSystemTimes - busy over
    // elapsed across all logical processors - never the PDH % Processor Utility, so it can read low
    // on a turbo machine but is the same number Task Manager shows. Always 0-100, and it rises above
    // zero when a thread is spinning.
    [Fact]
    public void Acct_HostLoad_CpuIsProcessorTimeInRange()
    {
        var host = new HostLoad();
        Spin(TimeSpan.FromMilliseconds(200));
        var sample = host.Sample();

        Assert.InRange(sample.CpuPercent, 0d, 100d);
        Assert.True(sample.CpuPercent > 0d, $"cpu {sample.CpuPercent}");
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
