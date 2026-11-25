// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Planner;

/// <summary>
/// The planner outbox's git and folder side (A11: PLAN-2, PLAN-6, PLAN-8; Planner-Step L1483,
/// Apply-Planner-Batch L1425). Test plan: scheduler-next/test-plans/planner.md.
/// </summary>
[ParallelMethods]
public class PlannerStepTests
{
    private const string NewCard = "# 020-B: y\n\nLane: B\nStatus: ready\n";

    private static object Write(string file = "020-B-y.md") =>
        new { op = "write", path = $"{PlannerHostFixture.Jobs}/queue/{file}", @base = "new", file };

    // PLAN-2: one step at most a minute, and only a fetch in fetchRepo.
    [Fact]
    public void Planner_Step_AtMostOncePerMinute_FetchOnly()
    {
        using var f = new PlannerHostFixture();
        var card = Path.Combine(f.Fx.Repo, "docs", "jobs", "queue", "010-A-x.md");
        File.AppendAllText(card, "my unsaved thought\n");
        var head = GitFixture.Git(f.Fx.Repo, "rev-parse", "HEAD");
        var status = GitFixture.Git(f.Fx.Repo, "status", "--porcelain");
        var t0 = f.Clock.Now;

        f.Fx.Push("other 1", new Dictionary<string, string?> { ["a.txt"] = "1\n" });
        f.Step.Run();
        Assert.Equal(f.RemoteSha(), GitFixture.Git(f.Fx.Repo, "rev-parse", "origin/main"));

        f.Fx.Push("other 2", new Dictionary<string, string?> { ["a.txt"] = "2\n" });
        var second = f.RemoteSha();
        f.Clock.Now = t0.AddSeconds(59);
        f.Step.Run();
        Assert.NotEqual(second, GitFixture.Git(f.Fx.Repo, "rev-parse", "origin/main"));

        f.Clock.Now = t0.AddSeconds(60);
        f.Step.Run();
        Assert.Equal(second, GitFixture.Git(f.Fx.Repo, "rev-parse", "origin/main"));

        // Fetch only: no pull, no checkout; the working tree is as the developer left it.
        Assert.Equal(head, GitFixture.Git(f.Fx.Repo, "rev-parse", "HEAD"));
        Assert.Equal(status, GitFixture.Git(f.Fx.Repo, "status", "--porcelain"));
        Assert.Contains("my unsaved thought", File.ReadAllText(card));
    }

    // PLAN-2: switched off, nothing is fetched and no batch is touched.
    [Fact]
    public void Planner_Step_SwitchedOff_DoesNothing()
    {
        using var f = new PlannerHostFixture { Push = false };
        var before = GitFixture.Git(f.Fx.Repo, "rev-parse", "origin/main");
        f.Fx.Push("other", new Dictionary<string, string?> { ["a.txt"] = "1\n" });
        f.Batch("001-a", "m", [Write()], new Dictionary<string, string> { ["020-B-y.md"] = NewCard });

        f.Step.Run();

        Assert.Equal(before, GitFixture.Git(f.Fx.Repo, "rev-parse", "origin/main"));
        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "001-a")));
    }

    // PLAN-6: quotes made single, blank line, trailers; the push rejected once, pushed on the second try.
    [Fact]
    public void Planner_Commit_MessageTrailersThreeTries()
    {
        using var f = new PlannerHostFixture();
        f.RejectFirstPush();
        f.Batch("001-a", "Plan \"x\"", [Write()], new Dictionary<string, string> { ["020-B-y.md"] = NewCard });

        f.Step.Run();

        Assert.True(File.Exists(Path.Combine(f.Fx.Bare, "rejected-once")));
        Assert.Equal("Plan 'x'\n\nCo-Authored-By: P <p@x>", GitFixture.Git(f.Fx.Bare, "log", "-1", "--format=%B", "main"));
        Assert.Equal("2", GitFixture.Git(f.Fx.Bare, "rev-list", "--count", "main"));
        Assert.Equal(NewCard, GitFixture.Git(f.Fx.Bare, "show", "main:docs/jobs/queue/020-B-y.md") + "\n");
        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "sent", "001-a")));
    }

    // PLAN-6: a batch that changes nothing is a success without a commit.
    [Fact]
    public void Planner_Commit_NothingToCommit_SuccessNoCommit()
    {
        using var f = new PlannerHostFixture();
        var before = f.RemoteSha();
        var same = new { op = "write", path = $"{PlannerHostFixture.Jobs}/queue/010-A-x.md", @base = "new", file = "010-A-x.md" };
        f.Batch("001-a", "again", [same], new Dictionary<string, string> { ["010-A-x.md"] = PlannerHostFixture.SeedCard });

        f.Step.Run();

        Assert.Equal(before, f.RemoteSha());
        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "sent", "001-a")));
    }

    // PLAN-6: a push rejected three times fails the batch.
    [Fact]
    public void Planner_Commit_RejectedThreeTimes_BatchFails()
    {
        using var f = new PlannerHostFixture();
        f.RejectEveryPush();
        f.Batch("001-a", "m", [Write()], new Dictionary<string, string> { ["020-B-y.md"] = NewCard });

        f.Step.Run();

        Assert.Contains("rejected three times", File.ReadAllText(Path.Combine(f.Outbox, "failed", "001-a", "reason.txt")));
    }

    // PLAN-8: a name clash in sent/ gets -HHmmss; a failure goes to failed/ with reason.txt; the page line.
    [Fact]
    public void Planner_BatchFolder_SentOrFailedWithClash()
    {
        using var f = new PlannerHostFixture();
        Directory.CreateDirectory(Path.Combine(f.Outbox, "sent", "001-a"));
        f.Batch("001-a", "ok", [Write()], new Dictionary<string, string> { ["020-B-y.md"] = NewCard });
        var bad = Path.Combine(f.Outbox, "002-b");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "batch.json"), "{ not json");
        Directory.CreateDirectory(Path.Combine(f.Outbox, "003-c"));   // no batch.json: still being written

        f.Step.Run();

        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "sent", "001-a-150405")));
        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "sent", "001-a")));
        Assert.False(Directory.Exists(Path.Combine(f.Outbox, "001-a")));
        Assert.Equal("batch.json is not valid JSON", File.ReadAllText(Path.Combine(f.Outbox, "failed", "002-b", "reason.txt")).Trim());
        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "003-c")));
        Assert.Contains(">on<", f.Step.Line());
        Assert.Contains("0 waiting", f.Step.Line());
        Assert.Contains("1 failed", f.Step.Line());
        Assert.StartsWith("sent 15:04", f.Step.Last);
        Assert.Contains("last:", f.Step.Line());
    }

    // PLAN-8: the page line while off, with batches waiting.
    [Fact]
    public void Planner_Line_OffAndWaiting()
    {
        using var f = new PlannerHostFixture { Push = false };
        f.Batch("001-a", "m", [Write()]);
        f.Batch("002-b", "m", [Write()]);

        Assert.Contains(">off<", f.Step.Line());
        Assert.Contains("2 waiting", f.Step.Line());
        Assert.DoesNotContain("failed", f.Step.Line());
    }
}
