// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Planner;

namespace Scheduler.Core.Tests.Planner;

/// <summary>PLAN-7 and PLAN-8: releasing ready cards, and how a batch is reported. Test plan: planner.md.</summary>
public class PlannerReleaseTests
{
    private const string A = "docs/jobs/queue/010-A-x.md";
    private const string B = "docs/jobs/queue/020-B-y.md";

    // PLAN-7
    [Fact]
    public void Planner_Release_ReadyCardsRetriesCleared()
    {
        var oldA = "# A\nStatus: waiting: dev\n";
        var oldB = "# B\nStatus: ready\n";
        var fx = new PlannerFixture().Main(A, oldA).Main(B, oldB)
            .InBatch("a.md", "# A\nStatus: ready\n\n## Result\n")
            .InBatch("b.md", "# B\nStatus: waiting: dev\n");
        var plan = fx.Batch("m",
            new { op = "write", path = A, @base = fx.Blob(oldA), file = "a.md" },
            new { op = "write", path = B, @base = fx.Blob(oldB), file = "b.md" }).Plan();
        Assert.True(plan.Ok, plan.Error);
        var retries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["010-A"] = 2, ["020-B"] = 1, ["030-C"] = 2 };
        var log = new ListLog();

        var tickNow = PlannerRelease.Clear(plan, retries, log);

        Assert.True(tickNow);
        Assert.False(retries.ContainsKey("010-A"));
        Assert.Equal(1, retries["020-B"]);
        Assert.Equal(2, retries["030-C"]);
        Assert.Equal(["planner: 010-A set ready, run count cleared"], log.Lines);
    }

    // PLAN-7: a ready card with no run count, or one the batch did not write, changes nothing.
    [Fact]
    public void Planner_Release_NothingToClear_NoTick()
    {
        var old = "# A\nStatus: ready\n";
        var fx = new PlannerFixture().Main(A, old).InBatch("a.md", "# A\nStatus: ready\n\nmore\n");
        var plan = fx.Batch("m", new { op = "write", path = A, @base = fx.Blob(old), file = "a.md" }).Plan();
        var retries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["030-C"] = 2 };
        var log = new ListLog();

        Assert.False(PlannerRelease.Clear(plan, retries, log));
        Assert.Equal(2, retries["030-C"]);
        Assert.Empty(log.Lines);
    }

    // PLAN-8, C1
    [Fact]
    public void Planner_FailedBatch_ReportedAsSent()
    {
        var fx = new PlannerFixture();
        fx.BatchJsonText = "{\"message\": \"jobs: close 1\", \"ops\": []}";
        var plan = fx.Plan();
        Assert.False(plan.Ok);

        var report = PlannerReport.For("001-a", plan, new DateTimeOffset(2026, 11, 18, 14, 5, 0, TimeSpan.Zero));

        // Spec C1: today's behaviour, kept until the developer decides (003-D review).
        Assert.True(report.Failed);
        Assert.Equal("batch.json has no ops", report.Reason);
        Assert.Equal("sent 14:05 jobs: close 1", report.Last);
        Assert.Equal(["planner: batch 001-a failed: batch.json has no ops", "planner: jobs: close 1"], report.LogLines);
    }

    // PLAN-8: a batch that went through is "sent" with its notes in the log.
    [Fact]
    public void Planner_Report_SentWithNotes()
    {
        var fx = new PlannerFixture().Main(B, "x\n");
        var plan = fx.Batch("jobs: tidy", new { op = "remove", path = A }, new { op = "remove", path = B }).Plan();

        var report = PlannerReport.For("001-a", plan, new DateTimeOffset(2026, 11, 18, 9, 7, 0, TimeSpan.Zero));

        Assert.False(report.Failed);
        Assert.Equal("sent 09:07 jobs: tidy", report.Last);
        Assert.Equal([$"planner: jobs: tidy ({A} was already gone)"], report.LogLines);
    }
}
