// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Planner;

/// <summary>
/// 1111-B (planner-outbox.md "Card numbers"): a batch's <c>new-{L}-{slug}.md</c> cards are numbered from the
/// pool's counter before the commit, in the same commit.
/// </summary>
[ParallelMethods]
public class PlannerNumberingTests
{
    private const string Queue = PlannerHostFixture.Jobs + "/queue/";

    private static object Write(string file, string path) => new { op = "write", path = Queue + path, @base = "new", file };

    private static string[] Tree(PlannerHostFixture f) =>
        GitFixture.Git(f.Fx.Bare, "ls-tree", "-r", "--name-only", "main").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string Show(PlannerHostFixture f, string path) => GitFixture.Git(f.Fx.Bare, "show", "main:" + path);

    [Fact]
    public void TwoBatches_EachWithANewCard_GetDistinctNumbers_AndReferencesAreRewritten()
    {
        using var f = new PlannerHostFixture();
        f.Step.Numbering = new CardNumbering(() => 1110);
        f.Step.Project = "Toolkit";
        f.Batch("20261122-190000-a", "jobs: cards a", [Write("a.md", "new-B-first.md"), Write("b.md", "new-B-second.md")],
            new Dictionary<string, string>
            {
                ["a.md"] = "# new-B: first\n\nStatus: ready\nDepends on: none\n",
                ["b.md"] = "# new-B: second\n\nStatus: ready\nDepends on: new-B-first, new-C-missing\n",
            });
        f.Batch("20261122-190100-b", "jobs: card c", [Write("c.md", "new-B-first.md")],
            new Dictionary<string, string> { ["c.md"] = "# new-B: first again\n\nStatus: ready\n" });

        Assert.True(f.Step.Run());

        var tree = Tree(f);
        Assert.Contains(Queue + "1111-B-first.md", tree);
        Assert.Contains(Queue + "1112-B-second.md", tree);
        Assert.Contains(Queue + "1113-B-first.md", tree);
        Assert.DoesNotContain(tree, p => p.Contains("/new-", StringComparison.Ordinal));
        Assert.StartsWith("# 1111-B: first", Show(f, Queue + "1111-B-first.md"));
        Assert.Contains("Depends on: 1111-B, new-C-missing", Show(f, Queue + "1112-B-second.md"));
        Assert.StartsWith("# 1113-B: first again", Show(f, Queue + "1113-B-first.md"));
        Assert.Equal("jobs: card c", GitFixture.Git(f.Fx.Bare, "log", "-1", "--format=%s", "main"));
        Assert.Contains("card numbered: Toolkit/new-B-first -> 1111-B-first", f.Log.Lines);
        Assert.Contains("card numbered: Toolkit/new-B-second -> 1112-B-second", f.Log.Lines);
        Assert.Contains("card numbered: Toolkit/new-B-first -> 1113-B-first", f.Log.Lines);
        Assert.Contains(f.Log.Lines, l => l.Contains("new-C-missing is named but not in batch 20261122-190000-a", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(f.Outbox, "sent", "20261122-190000-a")));
    }

    [Fact]
    public void Batch_CounterNotReady_WaitsInTheOutbox()
    {
        using var f = new PlannerHostFixture();
        f.Step.Numbering = new CardNumbering(() => null);
        var before = f.RemoteSha();
        f.Batch("20261122-190000-a", "jobs: card a", [Write("a.md", "new-B-first.md")],
            new Dictionary<string, string> { ["a.md"] = "# new-B: first\n" });

        Assert.False(f.Step.Run());

        Assert.Equal(before, f.RemoteSha());
        Assert.True(File.Exists(Path.Combine(f.Outbox, "20261122-190000-a", "batch.json")));
        Assert.Contains(f.Log.Lines, l => l.Contains("batch 20261122-190000-a waits for card numbers", StringComparison.Ordinal));
    }
}
