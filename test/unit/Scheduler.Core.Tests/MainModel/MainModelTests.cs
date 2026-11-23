using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Core.Tests.MainModel;

public class MainModelTests
{
    private static string CardText(string title, string status, string journal = "") =>
        $"# {title}\n\nCategory: A\nType: feature\nStatus: {status}\nPlan: docs/plan.md, step 2\n"
        + $"Depends on: 1-A\nTime box: 30 min\nWorker: Opus\nVersion: 3.0.1\n\n## Questions\n\n## Result\n{journal}";

    private static Dictionary<string, string> Repo(int done = 5)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobs/queue/1-A-one.md"] = CardText("One", "ready"),
            ["jobs/queue/2-B-two.md"] = CardText("Two", "waiting: developer"),
            ["jobs/active/3-A-three.md"] = CardText("Three", "ready"),
            ["jobs/review/4-C-four.md"] = CardText("Four", "done", "- 2026-11-22 01:00: built it, next: nothing\n"),
            ["jobs/archive/90-A-old.md"] = CardText("Old", "done"),
            ["jobs/requests/7-ask.md"] = "# Ask the planner\n\nStatus: open\nFor: 3-A\n",
            ["jobs/requests/README.md"] = "# Requests\n",
            ["jobs/scheduler.json"] = "{ \"poll\": 10 }",
            ["jobs/categories.json"] = "{ }",
            ["jobs/plans/p.md"] = "# Plan\n",
            ["src/x.cs"] = "class X {}",
            ["docs/plan.md"] = "# Plan\nStatus: started\n",
        };
        for (var i = 0; i < done; i++)
        {
            files[$"jobs/done/{100 + i}-A-d{i}.md"] = CardText("Done " + i, "done " + new string('x', 300));
        }

        return files;
    }

    private static string Describe(Card c) => string.Join(
        "|",
        c.Folder, c.Path, c.Name, c.Num, c.Category, c.Id, c.Title, c.Status, c.Prio, c.TimeBox, c.WorkerReq, c.ScopeLine,
        c.LocksLine, c.HeaderCategory, c.CardType, c.Version, c.PlanPath, c.PlanStep, string.Join(",", c.Depends),
        string.Join(";", c.Questions.Select(q => q.ToString())), c.Why, c.WhyFrom, c.LatestJournal, c.BlobId);

    private static string Describe(DoneCard c) => string.Join(
        "|",
        c.Folder, c.Path, c.Name, c.Num, c.Category, c.Id, c.BlobId, c.Title, c.Status, c.CardType, c.PlanPath, c.PlanStep,
        c.Version, string.Join(",", c.Depends), c.TimeBox, c.WorkerReq);

    private static string Describe(MainSnapshot s)
    {
        var lines = new List<string> { s.Commit, s.DoneLoaded.ToString() };
        lines.AddRange(s.Files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + f.Value));
        foreach (var dir in s.Layout.JobsDirs)
        {
            lines.AddRange(s.Cards(dir).Select(Describe));
            lines.AddRange(s.DoneCards(dir).Select(Describe));
            lines.AddRange(s.Requests(dir).Select(r => r.ToString()));
        }

        lines.AddRange(s.Layout.SettingsPaths.Concat(s.Watched).Order(StringComparer.Ordinal).Select(p => p + ":" + s.Settings(p)));
        return string.Join("\n", lines);
    }

    // MM-5: phase 1 gives open cards, requests and settings without opening a done card; phase 2
    // adds the done and archive headers, status cut as VW-13.
    [Fact]
    public void MM_5_PhaseOneJoinsBeforeDoneHeaders()
    {
        var files = Repo();
        var objects = new FakeObjects();
        var head = objects.Commit(files, null, "root", DateTimeOffset.UnixEpoch);
        var model = new RepositoryModel(objects, new MainLayout(["jobs"]), new FakeMain.FixedClock(DateTimeOffset.UnixEpoch));

        Assert.True(model.Load(head));

        Assert.Equal(["3-A", "1-A", "2-B", "4-C"], model.Cards("jobs").Select(c => c.Id));   // path order: active, queue, review
        Assert.Equal("waiting: developer", model.Cards("jobs")[2].Status);
        Assert.Equal(["7-ask"], model.Requests("jobs").Select(r => r.Name));
        Assert.Equal("{ \"poll\": 10 }", model.Settings("jobs/scheduler.json"));
        Assert.False(model.DoneLoaded);
        Assert.Empty(model.DoneCards("jobs"));
        var doneBlobs = files.Where(f => f.Key.Contains("/done/") || f.Key.Contains("/archive/"))
            .Select(f => FakeObjects.BlobId(f.Value)).ToHashSet();
        Assert.DoesNotContain(objects.BlobReads, doneBlobs.Contains);
        Assert.Contains(model.Entries("jobs/done"), e => e.Path == "jobs/done/100-A-d0.md");

        Assert.True(model.LoadDone());

        Assert.True(model.DoneLoaded);
        var headers = model.DoneCards("jobs");
        Assert.Equal(6, headers.Count);
        Assert.Equal("archive", headers.Single(h => h.Id == "90-A").Folder);
        var first = headers.Single(h => h.Id == "100-A");
        Assert.Equal("Done 0", first.Title);
        Assert.Equal(DoneCard.StatusLength, first.Status.Length);
        Assert.Equal(("docs/plan.md", "step 2"), (first.PlanPath, first.PlanStep));
        Assert.Equal(["1-A"], first.Depends);
        Assert.Equal((30, "Opus", "3.0.1", "feature"), (first.TimeBox, first.WorkerReq, first.Version, first.CardType));
    }

    // MM-7: only subtrees whose ids differ are read; a moved card keeps its blob and is not re-read;
    // a deleted card is dropped; done/ and the rest of the repository are not read unless changed.
    [Fact]
    public void MM_7_Advance_OnlyChangedSubtrees_MovedCardKeepsBlobNotReparsed_DeletedDropped_DoneUntouchedUnlessChanged()
    {
        var main = new FakeMain(Repo(done: 50));
        var before = main.Head;
        var doneTree = main.Objects.SubtreeOf(before, "jobs/done");
        var srcTree = main.Objects.SubtreeOf(before, "src");
        main.Objects.ResetCounts();

        main.Push(new Dictionary<string, string?>
        {
            ["jobs/queue/1-A-one.md"] = null,
            ["jobs/active/1-A-one.md"] = main.Files["jobs/queue/1-A-one.md"],
            ["jobs/queue/2-B-two.md"] = null,
            ["src/x.cs"] = "class Y {}",
        });

        Assert.Equal(main.Head, main.Commit);
        Assert.Empty(main.Objects.BlobReads);
        Assert.DoesNotContain(doneTree!, main.Objects.TreeReads);
        Assert.Equal(["1-A", "3-A", "4-C"], main.Cards("jobs").Select(c => c.Id).Order());
        var moved = main.Cards("jobs").Single(c => c.Id == "1-A");
        Assert.Equal(CardFolder.Active, moved.Folder);
        Assert.Equal("jobs/active/1-A-one.md", moved.Path);
        Assert.Equal(FakeObjects.BlobId(main.Files["jobs/active/1-A-one.md"]), moved.BlobId);
        Assert.Equal(50, main.DoneCards("jobs").Count(d => d.Folder == "done"));

        // src/ changed, but it is outside the jobs folders: its tree is listed in the log's
        // whole-tree diff, never in the model's own diff, so the model keeps no src/ path.
        Assert.DoesNotContain(main.Snapshot!.Files.Keys, p => p.StartsWith("src/", StringComparison.Ordinal));
        Assert.Contains(srcTree!, main.Objects.TreeReads);

        // One done card edited: only that blob is read.
        main.Objects.ResetCounts();
        main.Push(new Dictionary<string, string?> { ["jobs/done/107-A-d7.md"] = CardText("Seven", "done again") });
        Assert.Equal([FakeObjects.BlobId(CardText("Seven", "done again"))], main.Objects.BlobReads);
        Assert.Equal("Seven", main.DoneCards("jobs").Single(d => d.Id == "107-A").Title);
    }

    // MM-7 with MM-18: a card moved without being re-read is exactly what a fresh parse gives,
    // including CARD-13's Why, which depends on the folder.
    [Theory]
    [InlineData("jobs/review/4-C-four.md", "jobs/queue/4-C-four.md")]
    [InlineData("jobs/queue/2-B-two.md", "jobs/review/2-B-two.md")]
    [InlineData("jobs/queue/2-B-two.md", "jobs/active/2-B-two.md")]
    [InlineData("jobs/active/3-A-three.md", "jobs/review/3-A-three.md")]
    public void MM_7_MovedCard_EqualsFreshParse(string from, string to)
    {
        var main = new FakeMain(Repo());
        var text = main.Files[from];
        main.Objects.ResetCounts();

        main.Move(from, to);

        Assert.Empty(main.Objects.BlobReads);
        var fresh = CardFiles.ParseText(CardFiles.FromPath("jobs", to)!, text) with { BlobId = FakeObjects.BlobId(text) };
        Assert.Equal(Describe(fresh), Describe(main.Cards("jobs").Single(c => c.Path == to)));
    }

    // MM-7: open -> done keeps the parsed fields (no read); done -> open reads the blob once.
    [Fact]
    public void MM_7_MoveIntoAndOutOfDone_HeadersEqualFreshParse()
    {
        var main = new FakeMain(Repo());
        main.Objects.ResetCounts();

        main.Move("jobs/review/4-C-four.md", "jobs/done/4-C-four.md");
        Assert.Empty(main.Objects.BlobReads);
        var fresh = DoneHeaders.Parse(DoneHeaders.FromPath("jobs", "jobs/done/4-C-four.md")!, main.Files["jobs/done/4-C-four.md"], FakeObjects.BlobId(main.Files["jobs/done/4-C-four.md"]));
        Assert.Equal(Describe(fresh), Describe(main.DoneCards("jobs").Single(d => d.Id == "4-C")));

        main.Move("jobs/done/101-A-d1.md", "jobs/archive/101-A-d1.md");
        Assert.Empty(main.Objects.BlobReads);
        Assert.Equal("archive", main.DoneCards("jobs").Single(d => d.Id == "101-A").Folder);

        main.Move("jobs/done/102-A-d2.md", "jobs/queue/102-A-d2.md");
        Assert.Single(main.Objects.BlobReads);
        Assert.Equal(CardFolder.Queue, main.Cards("jobs").Single(c => c.Id == "102-A").Folder);
    }

    // MM-7 / CARD-3: an advance that cannot read an object keeps the previous snapshot; the next
    // ref read tries again.
    [Fact]
    public void MM_7_FailedAdvance_KeepsPreviousSnapshot()
    {
        var main = new FakeMain(Repo());
        var before = main.Snapshot;
        var next = main.Stage(new Dictionary<string, string?> { ["jobs/queue/5-A-five.md"] = CardText("Five", "ready") });
        main.Objects.Broken.Add(main.Objects.SubtreeOf(next, "jobs/queue")!);

        Assert.False(main.Model.RefRead(next));
        Assert.Same(before, main.Snapshot);
        Assert.Equal(1, main.Model.Failures);
        Assert.NotNull(main.Model.LastError);

        main.Objects.Broken.Clear();
        Assert.True(main.Model.RefRead(next));
        Assert.Equal(next, main.Commit);
        Assert.Equal(0, main.Model.Failures);
        Assert.Contains(main.Cards("jobs"), c => c.Id == "5-A");
    }

    // MM-7: whatever sequence of adds, edits, moves and deletes, the advanced snapshot is the one a
    // full load of the same commit gives (the Core half of MM_7_AdvanceParity_DiffEqualsFullReload).
    [Fact]
    public void MM_7_AdvanceParity_DiffEqualsFullReload()
    {
        var random = new Random(1060);
        var main = new FakeMain(Repo(done: 12));
        main.Settings("docs/plan.md");
        var folders = new[] { "queue", "active", "review", "done", "archive" };
        for (var step = 0; step < 60; step++)
        {
            var cards = main.Files.Keys.Where(p => p.EndsWith(".md", StringComparison.Ordinal) && folders.Any(f => p.StartsWith("jobs/" + f + "/", StringComparison.Ordinal))).ToList();
            var changes = new Dictionary<string, string?>();
            for (var n = random.Next(1, 4); n > 0; n--)
            {
                var pick = cards[random.Next(cards.Count)];
                if (changes.ContainsKey(pick))
                {
                    continue;
                }

                var name = pick[(pick.LastIndexOf('/') + 1)..];
                switch (random.Next(6))
                {
                    case 0:
                        changes[pick] = null;
                        break;
                    case 1:
                        changes[pick] = CardText("Edited " + step, random.Next(2) == 0 ? "ready" : "needs-human: why " + step);
                        break;
                    case 2:
                        changes[$"jobs/{folders[random.Next(folders.Length)]}/{200 + step}-B-new.md"] = CardText("New " + step, "ready", "- 2026-11-22 02:00: started\n");
                        break;
                    case 3:
                        changes["docs/plan.md"] = "# Plan\nStatus: step " + step + "\n";
                        break;
                    case 4:
                        changes["jobs/requests/" + step + "-r.md"] = "# R\nStatus: open\n";
                        break;
                    default:
                        var to = "jobs/" + folders[random.Next(folders.Length)] + "/" + name;
                        if (to != pick && !main.Files.ContainsKey(to) && !changes.ContainsKey(to))
                        {
                            changes[to] = main.Files[pick];
                            changes[pick] = null;
                        }

                        break;
                }
            }

            main.Push(changes);
            Assert.Equal(main.Head, main.Commit);
            var fresh = MainSnapshotBuilder.LoadDone(main.Objects, MainSnapshotBuilder.Load(main.Objects, main.Layout, main.Head, main.Snapshot!.Watched));
            Assert.Equal(Describe(fresh), Describe(main.Snapshot!));
        }
    }

    // MM-10 (with MM-8): a ref equal to the snapshot's commit, or unknown, reads nothing.
    [Fact]
    public void MM_10_RefUnchanged_NothingRead()
    {
        var main = new FakeMain(Repo());
        var before = main.Snapshot;
        main.Objects.ResetCounts();

        Assert.False(main.Model.RefRead(main.Head));
        Assert.False(main.Model.RefRead(null));
        _ = main.Cards("jobs");
        _ = main.DoneCards("jobs");
        _ = main.Settings("jobs/scheduler.json");
        _ = main.Entries("jobs/queue");

        Assert.Equal(0, main.Objects.Reads);
        Assert.Same(before, main.Snapshot);
    }

    // MM-11: the writer's commit on the snapshot's commit is applied from the listed paths only -
    // no tree diff, only the trees along those paths - and the blobs are parsed as committed.
    [Fact]
    public void MM_11_Apply_ParentIsSnapshot_Direct_BlobIdsComputed()
    {
        var main = new FakeMain(Repo(done: 30));
        var parent = main.Head;
        // As committed (core.autocrlf may turn the writer's LF into CRLF): the model must use this text.
        var committed = CardText("One", "review: check it").Replace("\n", "\r\n", StringComparison.Ordinal);
        var sha = main.Stage(new Dictionary<string, string?>
        {
            ["jobs/queue/1-A-one.md"] = null,
            ["jobs/review/1-A-one.md"] = committed,
        });
        var doneTree = main.Objects.SubtreeOf(sha, "jobs/done");
        main.Objects.ResetCounts();

        Assert.True(main.Model.Pushed(new PushedCommit(sha, parent, ["jobs/queue/1-A-one.md", "jobs/review/1-A-one.md"])));

        Assert.Equal(sha, main.Commit);
        Assert.DoesNotContain(doneTree!, main.Objects.TreeReads);
        Assert.Equal([FakeObjects.BlobId(committed)], main.Objects.BlobReads);
        var card = main.Cards("jobs").Single(c => c.Id == "1-A");
        Assert.Equal((CardFolder.Review, FakeObjects.BlobId(committed)), (card.Folder, card.BlobId));
        Assert.Equal(FakeObjects.BlobId(committed), main.Snapshot!.Files["jobs/review/1-A-one.md"]);
        Assert.False(main.Snapshot.Files.ContainsKey("jobs/queue/1-A-one.md"));
        var entry = Assert.Single(main.Commits(parent)!);
        Assert.Equal((sha, parent), (entry.Sha, entry.Parent));
        Assert.Equal(2, entry.Changes.Count);

        var fresh = MainSnapshotBuilder.LoadDone(main.Objects, MainSnapshotBuilder.Load(main.Objects, main.Layout, sha));
        Assert.Equal(Describe(fresh), Describe(main.Snapshot));
    }

    // MM-11: a parent newer than the snapshot (a worker pushed in between) is advanced to first.
    [Fact]
    public void MM_11_Apply_ParentNewer_AdvancesFirst()
    {
        var main = new FakeMain(Repo());
        var start = main.Head;
        var worker = main.Stage(new Dictionary<string, string?> { ["jobs/queue/6-A-six.md"] = CardText("Six", "ready") }, "worker");
        var ours = main.Stage(new Dictionary<string, string?> { ["jobs/queue/2-B-two.md"] = CardText("Two", "ready") }, "ours");

        Assert.True(main.Model.Pushed(new PushedCommit(ours, worker, ["jobs/queue/2-B-two.md"])));

        Assert.Equal(ours, main.Commit);
        Assert.Contains(main.Cards("jobs"), c => c.Id == "6-A");
        Assert.Equal("ready", main.Cards("jobs").Single(c => c.Id == "2-B").Status);
        Assert.Equal([worker, ours], main.Commits(start)!.Select(c => c.Sha));
    }

    // MM-11: a late Pushed for a commit the model is already past (reloaded at a newer main, so its
    // log does not hold the pushed commit) never moves the model back.
    [Fact]
    public void MM_11_Apply_PushedCommitIsAncestor_LogEmpty_ModelStays()
    {
        var main = new FakeMain(Repo());
        var parent = main.Head;
        var ours = main.Stage(new Dictionary<string, string?> { ["jobs/queue/2-B-two.md"] = CardText("Two", "ready") }, "ours");
        var newer = main.Stage(new Dictionary<string, string?> { ["jobs/queue/6-A-six.md"] = CardText("Six", "ready") }, "worker");
        Assert.True(main.Model.Load(newer));
        var before = main.Snapshot;
        Assert.Null(main.Commits(ours));

        Assert.False(main.Model.Pushed(new PushedCommit(ours, parent, ["jobs/queue/2-B-two.md"])));

        Assert.Same(before, main.Snapshot);
        Assert.Equal(newer, main.Commit);
        Assert.Contains(main.Cards("jobs"), c => c.Id == "6-A");
    }

    // MM-11: a rejected or failed push changes nothing and reads nothing.
    [Fact]
    public void MM_11_RejectedPush_ModelUnchanged()
    {
        var main = new FakeMain(Repo());
        var before = main.Snapshot;
        var sha = main.Stage(new Dictionary<string, string?> { ["jobs/queue/1-A-one.md"] = null });
        main.Objects.ResetCounts();

        Assert.False(main.Model.Pushed(new PushedCommit(sha, before!.Commit, ["jobs/queue/1-A-one.md"], Pushed: false)));

        Assert.Same(before, main.Snapshot);
        Assert.Equal(0, main.Objects.Reads);
    }

    // MM-12: the log keeps 10,000 commits or 30 days, whichever is more; Commits(since) answers
    // from it, and a commit it does not hold gives null (the caller falls back to git).
    [Fact]
    public void MM_12_CommitLog_CappedAt10000Or30Days()
    {
        var now = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
        static MainCommit At(int i, DateTimeOffset time) => new("c" + i, null, time, "s", []);

        var old = Enumerable.Range(0, 10_005).Select(i => At(i, now.AddDays(-40))).ToList();
        var capped = MainSnapshotBuilder.Cap(old, now);
        Assert.Equal(10_000, capped.Count);
        Assert.Equal("c5", capped[0].Sha);

        var recent = Enumerable.Range(0, 10_005).Select(i => At(i, now.AddDays(-29))).ToList();
        Assert.Equal(10_005, MainSnapshotBuilder.Cap(recent, now).Count);

        var mixed = Enumerable.Range(0, 10_010).Select(i => At(i, i < 3 ? now.AddDays(-31) : now.AddDays(-1))).ToList();
        Assert.Equal(10_007, MainSnapshotBuilder.Cap(mixed, now).Count);

        var few = Enumerable.Range(0, 5).Select(i => At(i, now.AddYears(-1))).ToList();
        Assert.Same(few, MainSnapshotBuilder.Cap(few, now));

        var main = new FakeMain(Repo());
        var start = main.Head;
        var a = main.Push(new Dictionary<string, string?> { ["src/x.cs"] = "a" }, "first");
        var b = main.Push(new Dictionary<string, string?> { ["jobs/plans/p.md"] = "b" }, "second");
        Assert.Equal([a, b], main.Commits(start)!.Select(c => c.Sha));
        Assert.Equal(["second"], main.Commits(a)!.Select(c => c.Subject));
        Assert.Empty(main.Commits(b)!);
        Assert.Null(main.Commits("not-a-commit"));
        Assert.Equal([new PathChange("src/x.cs", FakeObjects.BlobId("class X {}"), FakeObjects.BlobId("a"))], main.Commits(start)![0].Changes);
    }

    // MM-12 / section 4: a ref moved to an unrelated commit (a force push) restarts the log there.
    [Fact]
    public void MM_12_ForcePush_LogRestartsAtNewCommit()
    {
        var main = new FakeMain(Repo());
        main.Push(new Dictionary<string, string?> { ["src/x.cs"] = "a" });
        var unrelated = main.Objects.Commit(Repo(done: 2), null, "rewritten", DateTimeOffset.UnixEpoch);

        Assert.True(main.Model.RefRead(unrelated));

        Assert.Equal([unrelated], main.Commits(null)!.Select(c => c.Sha));
        Assert.Equal(2, main.DoneCards("jobs").Count(d => d.Folder == "done"));
    }

    // MM-6 and MM-4: a path outside the jobs folders is watched from its first ask and kept current;
    // a missing one answers null and is still watched; other text is read on demand.
    [Fact]
    public void MM_6_WatchedPath_KeptCurrent_MissingStaysWatched_TextOnDemand()
    {
        var main = new FakeMain(Repo());
        Assert.Equal("# Plan\nStatus: started\n", main.Settings("docs/plan.md"));
        Assert.Null(main.Settings("VERSION"));
        Assert.Contains("VERSION", main.Snapshot!.Watched);

        main.Push(new Dictionary<string, string?> { ["docs/plan.md"] = "# Plan\nStatus: done\n", ["VERSION"] = "3.0.72" });
        main.Objects.ResetCounts();
        Assert.Equal("# Plan\nStatus: done\n", main.Settings("docs/plan.md"));
        Assert.Equal("3.0.72", main.Settings("VERSION"));
        Assert.Equal(0, main.Objects.Reads);

        Assert.Equal(main.Files["jobs/queue/1-A-one.md"], main.Text("jobs/queue/1-A-one.md"));
        Assert.Equal([FakeObjects.BlobId(main.Files["jobs/queue/1-A-one.md"])], main.Objects.BlobReads);
        Assert.Equal("class X {}", main.Text("src/x.cs"));
        Assert.Null(main.Text("src/none.cs"));
        Assert.Null(main.Text("jobs/queue/none.md"));
    }

    // Section 1 / DL-22: projects sharing a repository share its model, each reading its own jobs folder.
    [Fact]
    public void TwoJobsFolders_OneModel_EachProjectItsOwn()
    {
        var files = new Dictionary<string, string>(Repo(done: 1), StringComparer.Ordinal)
        {
            ["tools/jobs/queue/1-A-other.md"] = CardText("Other", "ready"),
            ["tools/jobs/scheduler.json"] = "{ }",
        };
        var main = new FakeMain(files, ["jobs", "tools/jobs"]);

        Assert.Equal(["Other"], main.Cards("tools/jobs").Select(c => c.Title));
        Assert.Equal(4, main.Cards("jobs").Count);
        Assert.Equal("{ }", main.Settings("tools/jobs/scheduler.json"));
        Assert.Empty(main.Requests("tools/jobs"));
    }

    // MM-14 (ScopeAudit): every path that differs between two commits, from an in-process diff.
    [Fact]
    public void Changed_ListsEveryPathBetweenTwoCommits()
    {
        var main = new FakeMain(Repo());
        var a = main.Head;
        main.Push(new Dictionary<string, string?> { ["src/x.cs"] = "a", ["jobs/queue/1-A-one.md"] = null });
        var b = main.Push(new Dictionary<string, string?> { ["docs/new.md"] = "n" });

        Assert.Equal(["docs/new.md", "jobs/queue/1-A-one.md", "src/x.cs"], main.Changed(a, b));
        Assert.Null(main.Changed(a, "missing"));
    }
}
