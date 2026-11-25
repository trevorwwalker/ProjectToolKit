// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 1195-C: the Control window's host-load line (ACCT-9) rounds its CPU percent the way the
/// tick line does. Host's <c>HostLoad.ShownCpuPercent</c> rounds half away from zero and clamps to
/// 0-100; the page used plain <c>Math.Round</c>, which rounds half to even, so at 80.5 the top bar
/// said 80% while the log said 81%. Web cannot reference Host, so the two must match by inspection.
/// </summary>
public sealed class PageHostLoadTests
{
    private static string Render(double cpuPercent) =>
        new PageRenderer().Render(
            ViewsFixture.Plain() with
            {
                HostLoad = new HostLoadSample
                {
                    CpuPercent = cpuPercent,
                    FreeMemoryBytes = 12L * 1024 * 1024 * 1024,
                    TotalMemoryBytes = 32L * 1024 * 1024 * 1024,
                    Cores = 8,
                },
            },
            remote: false,
            controlsAllowed: true);

    // Half-way values go up, never to the nearest even number (1195-C).
    [Theory]
    [InlineData(80.5, "CPU 81%")]
    [InlineData(81.5, "CPU 82%")]
    [InlineData(0.5, "CPU 1%")]
    [InlineData(42.4, "CPU 42%")]
    [InlineData(42.6, "CPU 43%")]
    public void HostLoad_CpuPercent_RoundsAwayFromZero(double cpuPercent, string expected)
    {
        Assert.Contains(expected, Render(cpuPercent), StringComparison.Ordinal);
    }

    // The percent stays inside 0-100 whatever the sample says.
    [Theory]
    [InlineData(-0.6, "CPU 0%")]
    [InlineData(99.6, "CPU 100%")]
    [InlineData(100.4, "CPU 100%")]
    [InlineData(137.0, "CPU 100%")]
    public void HostLoad_CpuPercent_ClampedTo0To100(double cpuPercent, string expected)
    {
        Assert.Contains(expected, Render(cpuPercent), StringComparison.Ordinal);
    }

    // The rest of the line is unchanged: memory in whole gigabytes and the thread count.
    [Fact]
    public void HostLoad_Line_MemoryAndThreads()
    {
        Assert.Contains("CPU 43%, 12G free of 32G, 8 threads", Render(42.6), StringComparison.Ordinal);
    }
}
