// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Host.Processes;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 947-B (the developer, 2026-11-19: "a settings change must never cascade into failures"). Renaming
/// or removing a worker type on the Worker types panel must not strand a card: a rename takes the
/// cards that name the old name with it, in the same commit as workers.json; a worker already running
/// under the old name keeps running and is recorded under the new one, so a restart adopts it there;
/// and a type that would leave while open cards name it is refused with those cards named.
/// Test plan: scheduler-next/test-plans/worker-types.md (WT-P3, WT-C7, WT-C11).
/// </summary>
public class WorkerTypeRenameTests
{
    private const string Jobs = "docs/jobs";
    private const string WorkersRel = Jobs + "/workers.json";

    private const string Categories = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    // workers.json in the pool form the panel's Save writes, so the types the panel shows are named.
    private static string PoolOf(int total, params (string Name, int Max)[] types) =>
        WorkerTypesJson.BuildPool(new WorkerPool
        {
            MaxWorkers = total,
            Types = [.. types.Select(t => new WorkerType { Name = t.Name, Max = t.Max })],
        });

    // A card with its Worker: line in the header block (CARD-10), and a body of its own.
    private static string CardOf(string title, string category, string workerLine, string body = "Do the thing.\n") =>
        $"# {title}\n\nLane: {category}\nStatus: ready\n{workerLine}Priority: normal\n\n## Task\n\n{body}";

    private static PageWorkerTypeView Row(string loaded, string now, int max) =>
        new() { Name = now, OriginalName = loaded, Max = max };

    /// <summary>
    /// 947-B rule 2: renaming a type rewrites the <c>Worker:</c> line of every open card that names it
    /// - case-insensitively, with a "(reason)" after the value kept - in the one commit that carries
    /// workers.json, whose message lists the cards. A card that names a model, one with no Worker:
    /// line, a <c>Worker:</c> sentence in the Task, and a done/ card are all left alone.
    /// </summary>
    [Fact]
    public void WorkerTypes_RenameWithOpenCards_OneCommitRewritesTheirWorkerLines()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;
        main.Files[$"{Jobs}/queue/951-A-old-name.md"] = CardOf("951-A: Old name", "A", "Worker: worker\n");
        main.Files[$"{Jobs}/active/952-B-old-name.md"] = CardOf(
            "952-B: Old name",
            "B",
            "Worker: worker (needs the model server)\n",
            "Worker: worker takes the free model, never the premium one.\n");
        main.Files[$"{Jobs}/review/953-C-old-name.md"] = CardOf("953-C: Old name", "C", "Worker: WORKER\n");
        main.Files[$"{Jobs}/queue/954-D-model-name.md"] = CardOf("954-D: Model name", "D", "Worker: claude-sonnet-5-5\n");
        main.Files[$"{Jobs}/queue/955-E-no-line.md"] = CardOf("955-E: No line", "E", "");
        main.Files[$"{Jobs}/done/956-E-old-name.md"] = "# 956-E: Long done\n\nWorker: worker\n";

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-rename");
        harness.Tick_Once();

        var saved = harness.Actions.SaveWorkerTypes(3, [Row("worker", "default", 2), Row("Sonnet", "Sonnet", 1)]);

        Assert.True(saved.Ok);
        var change = Assert.Single(committer.Changes);
        Assert.StartsWith("scheduler: workers - types default 2, Sonnet 1 of 3", change.Message, StringComparison.Ordinal);
        Assert.Contains("renamed worker to default", change.Message, StringComparison.Ordinal);
        Assert.Contains("951-A", change.Message, StringComparison.Ordinal);
        Assert.Contains("952-B", change.Message, StringComparison.Ordinal);
        Assert.Contains("953-C", change.Message, StringComparison.Ordinal);

        // One commit: workers.json and the three cards, and nothing else.
        Assert.Equal(4, change.Writes.Count);
        Assert.Contains("\"default\"", change.Writes[WorkersRel], StringComparison.Ordinal);
        Assert.DoesNotContain("\"worker\"", change.Writes[WorkersRel], StringComparison.Ordinal);

        var renamed = change.Writes[$"{Jobs}/queue/951-A-old-name.md"];
        Assert.Contains("Worker: default\n", renamed, StringComparison.Ordinal);
        Assert.Contains("Status: ready", renamed, StringComparison.Ordinal);
        Assert.Contains("## Task", renamed, StringComparison.Ordinal);

        // The value only: the reason after it stays, and the Task's own "Worker: worker" sentence is
        // not a field (760-A) and is not touched.
        var withReason = change.Writes[$"{Jobs}/active/952-B-old-name.md"];
        Assert.Contains("Worker: default (needs the model server)", withReason, StringComparison.Ordinal);
        Assert.Contains("Worker: worker takes the free model", withReason, StringComparison.Ordinal);
        Assert.DoesNotContain("Worker: default takes", withReason, StringComparison.Ordinal);

        // Case-insensitive match; the card takes the name as the panel now spells it.
        Assert.Contains("Worker: default\n", change.Writes[$"{Jobs}/review/953-C-old-name.md"], StringComparison.Ordinal);

        // A Worker: line naming a model (rule 5), a card with no Worker: line, and a done/ card.
        Assert.False(change.Writes.ContainsKey($"{Jobs}/queue/954-D-model-name.md"));
        Assert.False(change.Writes.ContainsKey($"{Jobs}/queue/955-E-no-line.md"));
        Assert.False(change.Writes.ContainsKey($"{Jobs}/done/956-E-old-name.md"));
    }

    /// <summary>
    /// 947-B rule 3: a worker running under the old name keeps running, is moved to the new name in
    /// memory, and running.json is rewritten at the save rather than at the next tick - so start-up
    /// adopts it under the name the panel has now (ADOPT-1, WT-W5).
    /// </summary>
    [Fact]
    public void WorkerTypes_RenameWithRunningWorker_AdoptedUnderTheNewName()
    {
        var pool = PoolOf(2, ("worker", 2));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, project: "ptk-947b-run");
        harness.Tick_Once();

        harness.Running(1, card: "951-A-old-name");
        harness.Tick.Adopt(harness.Tick.Running[1] with { Type = "worker" });

        var saved = harness.Actions.SaveWorkerTypes(2, [Row("worker", "default", 2)]);

        Assert.True(saved.Ok);
        Assert.Equal("default", harness.Tick.Running[1].Type);

        var running = harness.Read("running.json");
        Assert.NotNull(running);
        Assert.Contains("\"type\": \"default\"", running, StringComparison.Ordinal);

        // The restart: the same running.json, the pool without the old name, the worker still alive.
        var adopter = new WorkerAdopter(harness.Files, harness.Processes, harness.Clock, harness.Log);
        var adopted = adopter.Adopt(new WorkerPool
        {
            MaxWorkers = 2,
            Types = [new WorkerType { Name = "default", Max = 2 }],
        });

        var entry = Assert.Single(adopted.Entries);
        Assert.Equal("default", entry.Worker.Type);
        Assert.Equal("951-A-old-name", entry.Worker.Card);
    }

    /// <summary>
    /// 947-B rule 4: taking out a type that open cards name is refused, and the page is told which
    /// cards hold it back. Nothing is committed and workers.json keeps the type.
    /// </summary>
    [Fact]
    public void WorkerTypes_RemoveTypeUsedByOpenCards_RefusedNamingTheCards()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;
        main.Files[$"{Jobs}/queue/951-A-old-name.md"] = CardOf("951-A: Premium work", "A", "Worker: Sonnet\n");
        main.Files[$"{Jobs}/active/952-B-old-name.md"] = CardOf("952-B: Premium work", "B", "Worker: sonnet\n");
        main.Files[$"{Jobs}/done/953-C-old-name.md"] = "# 953-C: Done premium\n\nWorker: Sonnet\n";

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-gone");
        harness.Tick_Once();

        var refused = harness.Actions.SaveWorkerTypes(2, [Row("worker", "worker", 2)]);

        Assert.False(refused.Ok);
        Assert.Equal(
            "type Sonnet is used by 951-A, 952-B: change their Worker: lines or keep the type",
            refused.Error);
        Assert.Contains(harness.Log.Lines, line => line.Contains("type Sonnet is used by 951-A, 952-B", StringComparison.Ordinal));
        Assert.Empty(committer.Changes);
        Assert.Equal(pool, main.Files[WorkersRel], StringComparer.Ordinal);
    }

    /// <summary>
    /// 947-B rule 4: a type no open card names leaves as it always did, and setting a cap to 0 is only
    /// turning a type off - it saves whatever the cards name.
    /// </summary>
    [Fact]
    public void WorkerTypes_RemoveUnusedType_Saves()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1), ("Opus", 0));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;
        main.Files[$"{Jobs}/queue/951-A-old-name.md"] = CardOf("951-A: Free work", "A", "Worker: worker\n");

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-drop");
        harness.Tick_Once();

        // Sonnet is taken out; Opus stays at cap 0, which is off, not gone; and one card names worker.
        var saved = harness.Actions.SaveWorkerTypes(2, [Row("worker", "worker", 2), Row("Opus", "Opus", 0)]);

        Assert.True(saved.Ok);
        var change = Assert.Single(committer.Changes);
        var text = Assert.Single(change.Writes);
        Assert.Equal(WorkersRel, text.Key);
        Assert.Contains("\"worker\"", text.Value, StringComparison.Ordinal);
        Assert.Contains("\"Opus\"", text.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Sonnet", text.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// 947-B Q2 (the developer, 2026-11-19): a type whose worker is still running cannot be taken out of
    /// the list either - the panel greys its Remove button, but a Save that simply leaves the row out
    /// used to save, leaving a worker that after a restart has no type at all. Refused, and the worker
    /// keeps running under a type that is still there.
    /// </summary>
    [Fact]
    public void WorkerTypes_RemoveTypeWithRunningWorker_RefusedNamingTheWorker()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-live");
        harness.Tick_Once();

        harness.Running(2, card: "952-B-premium");
        harness.Tick.Adopt(harness.Tick.Running[2] with { Type = "Sonnet" });

        // Sonnet's row left out of the save; its worker is still alive in slot 2.
        var refused = harness.Actions.SaveWorkerTypes(2, [Row("worker", "worker", 2)]);

        Assert.False(refused.Ok);
        Assert.Equal("type Sonnet has slot 2 (952-B-premium) running: stop it or keep the type", refused.Error);
        Assert.Contains(harness.Log.Lines, line => line.Contains("type Sonnet has slot 2", StringComparison.Ordinal));
        Assert.Empty(committer.Changes);
        Assert.Equal(pool, main.Files[WorkersRel], StringComparer.Ordinal);
        Assert.Equal("Sonnet", harness.Tick.Running[2].Type);
    }

    /// <summary>
    /// 947-B rule 4 and Q2 together: what a running worker does not allow is the type <em>leaving</em>;
    /// turning it off (cap 0) is only off, so it saves with its worker still running.
    /// </summary>
    [Fact]
    public void WorkerTypes_OffTypeWithRunningWorker_Saves()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-off");
        harness.Tick_Once();

        harness.Running(2, card: "952-B-premium");
        harness.Tick.Adopt(harness.Tick.Running[2] with { Type = "Sonnet" });

        var saved = harness.Actions.SaveWorkerTypes(2, [Row("worker", "worker", 2), Row("Sonnet", "Sonnet", 0)]);

        Assert.True(saved.Ok);
        var change = Assert.Single(committer.Changes);
        var written = Assert.Single(change.Writes);
        Assert.Equal(WorkersRel, written.Key);
        Assert.Contains("\"max\": 0", written.Value, StringComparison.Ordinal);
        Assert.Equal("Sonnet", harness.Tick.Running[2].Type);
    }

    /// <summary>
    /// 947-B: a save that only changes caps touches workers.json alone - no card is rewritten, and the
    /// message says nothing about cards, even though open cards name the type.
    /// </summary>
    [Fact]
    public void WorkerTypes_CapChangeOnly_WritesOnlyWorkersJson()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;
        main.Files[$"{Jobs}/queue/951-A-old-name.md"] = CardOf("951-A: Free work", "A", "Worker: worker\n");
        main.Files[$"{Jobs}/active/952-B-premium.md"] = CardOf("952-B: Premium work", "B", "Worker: Sonnet\n");

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-caps");
        harness.Tick_Once();

        var saved = harness.Actions.SaveWorkerTypes(3, [Row("worker", "worker", 1), Row("Sonnet", "Sonnet", 2)]);

        Assert.True(saved.Ok);
        var change = Assert.Single(committer.Changes);
        var written = Assert.Single(change.Writes);
        Assert.Equal(WorkersRel, written.Key);
        Assert.Contains("\"max\": 1", written.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("renamed", change.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Worker: line", change.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 947-B rule 1: a row Add type made carries no original name, so a new type is not read as the
    /// renaming of the type whose row was removed next to it.
    /// </summary>
    [Fact]
    public void WorkerTypes_NewRowWithNoOriginalName_IsNotARename()
    {
        var pool = PoolOf(3, ("worker", 2), ("Sonnet", 1));
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[WorkersRel] = pool;
        main.Files[$"{Jobs}/queue/951-A-old-name.md"] = CardOf("951-A: Free work", "A", "Worker: worker\n");

        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(
            categoriesJson: Categories, workersJson: pool, main: main, committer: committer, project: "ptk-947b-add");
        harness.Tick_Once();

        // Sonnet's row removed and a new type added in its place; worker's row untouched.
        var saved = harness.Actions.SaveWorkerTypes(3,
        [
            Row("worker", "worker", 2),
            new PageWorkerTypeView { Name = "Opus", Max = 1 },
        ]);

        Assert.True(saved.Ok);
        var change = Assert.Single(committer.Changes);
        var written = Assert.Single(change.Writes);
        Assert.Equal(WorkersRel, written.Key);
        Assert.Contains("\"Opus\"", written.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("renamed", change.Message, StringComparison.Ordinal);
        Assert.Contains("Worker: worker\n", main.Files[$"{Jobs}/queue/951-A-old-name.md"], StringComparison.Ordinal);
    }
}
