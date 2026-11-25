// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.PageActions;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Program;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 1084-B (the developer's review of 2026-11-22: the in-memory model was checked against git, but not the page).
/// The page shows the tick's last card list with <see cref="PageCardOverlay"/> on top (990-B) until the next card
/// read, and the model is moved by the writer after its push (MM-11). These tests run the composed page actions
/// against a real answers worktree over a seeded repository, with the tick reading main through the real
/// <see cref="CardReader"/> fed by the model, and check the page three times - right after the action returns,
/// after the next tick, and after the overlay's keep has passed: each time the card is where main has it (read
/// with git from the bare remote, not from the model), once, with main's Status.
/// </summary>
[ParallelMethods]
public sealed class PageConsistencyAfterWritesTests
{
    private const string Jobs = "docs/jobs";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static string Card(string title, string status, params string[] questions) =>
        $"# {title}\n\nCategory: B\nStatus: {status}\n\n## Task\n\nDo it.\n\n## Questions\n\n" +
        string.Concat(questions.Select(q => q + "\n")) +
        "\n## Result\n\n- 2026-11-21 19:01 Worked and put the card up for review.\n" +
        "- 2026-11-21 20:30 developer: sent back from review from the page; read any ## Dev section first.\n" +
        "- 2026-11-21 22:06 Resumed, fixed it, and put it up for review again.\n";

    // 1057-B's case: Q1 was answered "-> queue" and acted on in an earlier round (the card went back and came
    // round again); the open questions after it close the card to done.
    private const string OldQueueAnswer =
        "- Q1: Found: the box saves. Question: does the number stay? [yes -> done | no, send it back -> queue] -> **no, send it back** (developer, 2026-11-21 19:40)";

    private static Dictionary<string, string?> Seed() => new(StringComparer.Ordinal)
    {
        [Jobs + "/review/101-B-done-me.md"] = Card("101-B: Done me", "review: look at it"),
        [Jobs + "/review/102-B-send-back.md"] = Card("102-B: Send back", "review: look at it"),
        [Jobs + "/active/103-B-answer-done.md"] = Card("103-B: Answer done", "needs-human: one question",
            "- Q1: Found: a. Question: finish? [finish -> done | leave it]"),
        [Jobs + "/active/104-B-answer-queue.md"] = Card("104-B: Answer queue", "needs-human: one question",
            "- Q1: Found: b. Question: again? [again -> queue | leave it]"),
        [Jobs + "/queue/105-B-retry.md"] = Card("105-B: Retry", "ready"),
        [Jobs + "/review/106-B-one-open.md"] = Card("106-B: One open", "review: answer Q2", OldQueueAnswer,
            "- Q2: Found: the saves land. Question: do they stay now? [yes, they stay -> done | no, they still reset -> queue]"),
        [Jobs + "/review/107-B-two-open.md"] = Card("107-B: Two open", "review: answer Q2 and Q3", OldQueueAnswer,
            "- Q2: Found: the saves land. Question: do they stay now? [yes, they stay -> done | no, they still reset -> queue]",
            "- Q3: Found: a second change is kept. Question: does 6 stay at 6? [yes -> done | no, it still resets -> queue]"),
        [Jobs + "/review/108-B-planned.md"] = Card("108-B: Planned", "review: look at it"),
    };

    [Fact]
    public void Done_OnAReviewCard_PageFollowsMain()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Done("101-B-done-me"), "101-B", CardFolder.Done);
    }

    // 1099-C: the note typed in a card's box is filed in that card whichever button is pressed, and in no other.
    [Fact]
    public void DoneWithANote_FilesItInTheDoneCard_AndInNoOther()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Done("101-B-done-me", "went fine, thanks"), "101-B", CardFolder.Done);

        Assert.Contains("developer (from the page): went fine, thanks", p.MainText(Jobs + "/done/101-B-done-me.md"));
        Assert.DoesNotContain("went fine", p.MainText(Jobs + "/review/102-B-send-back.md"));
    }

    [Fact]
    public void RetryWithANote_FilesItInTheCard_WhichStaysWhereItIs()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Retry("105-B-retry", "try it again"), "105-B", CardFolder.Queue);

        var text = p.MainText(Jobs + "/queue/105-B-retry.md");
        Assert.Contains("developer (from the page): try it again", text);
        Assert.Contains("Status: ready", text);
    }

    [Fact]
    public void SaveNote_FilesItWithoutMovingTheCard_OrChangingItsStatus()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.SaveNote("102-B-send-back", "for later"), "102-B", CardFolder.Review);

        var text = p.MainText(Jobs + "/review/102-B-send-back.md");
        Assert.Contains("developer (from the page): for later", text);
        Assert.Contains("Status: review: look at it", text);
        Assert.DoesNotContain("for later", p.MainText(Jobs + "/review/101-B-done-me.md"));
    }

    [Fact]
    public void SaveNote_Empty_IsRefused_AndDoneWithoutANoteAddsNoLine()
    {
        using var p = new Page();
        Assert.False(p.Actions.SaveNote("102-B-send-back", "  ").Ok);
        Assert.True(p.Actions.Done("101-B-done-me", "").Ok);

        Assert.DoesNotContain("developer (from the page)", p.MainText(Jobs + "/done/101-B-done-me.md"));
    }

    [Fact]
    public void SendBack_PageFollowsMain()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.ResumeCard("102-B-send-back"), "102-B", CardFolder.Active);
    }

    [Fact]
    public void AnswerToDone_PageFollowsMain()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Answer("103-B-answer-done", 1, "0", ""), "103-B", CardFolder.Done);
    }

    [Fact]
    public void AnswerToQueue_PageFollowsMain()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Answer("104-B-answer-queue", 1, "0", ""), "104-B", CardFolder.Queue);
    }

    [Fact]
    public void Retry_PageFollowsMain()
    {
        using var p = new Page();
        p.Tick.Retries["105-B"] = 3;
        p.ActAndCheck(() => p.Actions.Retry("105-B-retry"), "105-B", CardFolder.Queue);
        Assert.False(p.Tick.Retries.ContainsKey("105-B"));
    }

    [Fact]
    public void PlannerBatchThatMovesACard_PageFollowsMain()
    {
        using var p = new Page();
        p.PlannerMoves("108-B", Jobs + "/review/108-B-planned.md");
        p.CheckAfterTheTick("108-B", CardFolder.Queue);
    }

    // 1057-B, one open question: the old "-> queue" must not send the card back; it goes to done/.
    [Fact]
    public void AnswerToDone_OnAReviewCardWithOneOpenQuestion_GoesToDone()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Answer("106-B-one-open", 2, "0", ""), "106-B", CardFolder.Done);
    }

    // 1057-B, two open questions: the first answer leaves the card in review/ with Q3 open, the second
    // closes it to done/, and the page follows main at each step.
    [Fact]
    public void AnswerToDone_OnAReviewCardWithTwoOpenQuestions_GoesToDone()
    {
        using var p = new Page();
        p.ActAndCheck(() => p.Actions.Answer("107-B-two-open", 2, "0", ""), "107-B", CardFolder.Review);
        Assert.Equal([3], p.Shown("107-B").Questions.Select(q => q.Number));
        p.ActAndCheck(() => p.Actions.Answer("107-B-two-open", 3, "0", ""), "107-B", CardFolder.Done);
    }

    // Main moves the card on before the tick's next read: a planner batch takes the sent-back card out of
    // active/ to queue/. The overlay's landing (active/) is then never what the tick reads, so it must not hold
    // the page - before 1084-B it drew the card twice, in queue/ from main and in active/ from the overlay.
    [Fact]
    public void SendBack_ThenAPlannerBatchMovesItOn_PageFollowsMain()
    {
        using var p = new Page();
        Assert.True(p.Actions.ResumeCard("102-B-send-back").Ok);
        p.PlannerMoves("102-B", Jobs + "/active/102-B-send-back.md", "queue");
        p.CheckAfterTheTick("102-B", CardFolder.Queue);
    }

    // Main changes the card's Status where it is before the tick's next read: after Q2 the card stays in review/
    // with Q3 open, and then a status commit sets it waiting. The page must show main's Status, not the answer's.
    [Fact]
    public void Answer_ThenMainChangesTheStatusInPlace_PageFollowsMain()
    {
        using var p = new Page();
        Assert.True(p.Actions.Answer("107-B-two-open", 2, "0", "").Ok);
        var path = Jobs + "/review/107-B-two-open.md";
        var text = p.MainText(path).Replace("Status: review: answer Q2 and Q3", "Status: review: asked again", StringComparison.Ordinal);
        Assert.Contains("Status: review: asked again", text, StringComparison.Ordinal);
        Assert.True(p.Committer.Apply(new JobsChange
        {
            Message = "jobs: status 107-B-two-open",
            Writes = new Dictionary<string, string>(StringComparer.Ordinal) { [path] = text },
        }).Ok);
        p.CheckAfterTheTick("107-B", CardFolder.Review);
        Assert.Equal("review: asked again", p.Shown("107-B").Status);
    }

    /// <summary>The composed page over a seeded repository with a real answers worktree.</summary>
    private sealed class Page : IDisposable
    {
        private readonly GitFixture _fx = new();
        private readonly MainModels _models = new(1);
        private readonly MainModelsRepository _repo;
        private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 22, 14, 0, 0, TimeSpan.Zero));
        private readonly ActionTickHost _host = new();
        private readonly SchedulerConfig _config;
        private readonly PageCardOverlay _overlay;
        private readonly PageSnapshotSources _sources;

        public Page()
        {
            _fx.Push("seed", Seed());
            GitFixture.Git(_fx.Repo, "config", "user.name", "scheduler");
            GitFixture.Git(_fx.Repo, "config", "user.email", "s@s");
            GitFixture.Git(_fx.Repo, "config", "commit.gpgsign", "false");
            GitFixture.Git(_fx.Repo, "pull", "-q", "origin", "main");
            var root = Path.GetDirectoryName(_fx.Repo)!;
            var logDir = Path.Combine(root, "logs");
            Directory.CreateDirectory(logDir);
            _config = new SchedulerConfig
            {
                Project = "ptk-1084b",
                JobsDir = Jobs,
                LogDir = logDir,
                Repo = _fx.Repo,
                WorkspaceRoot = Path.Combine(root, "slots"),
                Slots = 2,
                PageStopGraceMinutes = 5,
                PollSeconds = 1,
                Agents = new Dictionary<string, AgentDefinition>(),
            };
            var log = new ListLog();
            _repo = _models.Join(new MainModelsJoin(_fx.Repo, "origin", "main", Jobs, TimeSpan.FromSeconds(15), false,
                new GitRunner(), log, new FakeClock(DateTimeOffset.Now)));
            Assert.True(_repo.Loading.Wait(Wait), "load did not end");
            Assert.NotNull(_repo.Model);
            Committer = new AnswersCommitter(new GitRunner(), _fx.Repo, Path.Combine(root, "slots", "answers"), "origin", "main")
            {
                MainModel = () => _repo.Model,
            };
            var reader = new CardReader(_fx.Main(), Jobs, log) { ByCommit = true, Feed = _repo };
            _host.ReadCardsFrom = reader.Read;
            var files = new StateFiles(logDir);
            Tick = new SchedulerTick(_config, _clock, log, new TickLoad(), new TickProbe(), new ActionProcesses(), files, _host);
            Tick.Run();

            _overlay = new PageCardOverlay(Jobs, _clock);
            _sources = new PageSnapshotSources { Overlay = _overlay };
            Actions = new HostPageActions(new PageActionParts
            {
                Config = _config,
                Clock = _clock,
                Log = log,
                Files = files,
                Tick = Tick,
                Committer = Committer,
                Processes = new ActionProcesses(),
                Main = _fx.Main(),
                // The tick runs only when a test says so: in between, the page has the overlay and the tick's last read.
                TickNow = () => { },
                Overlay = _overlay,
            })
            {
                Feed = _repo,
            };
        }

        public AnswersCommitter Committer { get; }

        public SchedulerTick Tick { get; }

        public HostPageActions Actions { get; }

        public void Dispose()
        {
            _models.Leave(_repo, Jobs);
            _fx.Dispose();
        }

        /// <summary>A planner batch, committed through the same writer the planner step uses: the card leaves for another folder, ready.</summary>
        public CommitResult PlannerMoves(string id, string from, string to = "queue")
        {
            var name = Path.GetFileName(from);
            var dest = $"{Jobs}/{to}/{name}";
            var text = MainText(from);
            var status = text.Split('\n').First(l => l.StartsWith("Status:", StringComparison.Ordinal));
            var result = Committer.Apply(new JobsChange
            {
                Message = $"jobs: planner - {id} to {to}",
                Moves = [new FileMove { From = from, To = dest }],
                Writes = new Dictionary<string, string>(StringComparer.Ordinal) { [dest] = text.Replace(status, "Status: ready", StringComparison.Ordinal) },
            });
            Assert.True(result.Ok, result.Error);
            return result;
        }

        /// <summary>Runs a page action, then checks the page right after it, after the next tick and after the overlay's keep.</summary>
        public void ActAndCheck(Func<object> act, string id, CardFolder folder)
        {
            var result = act();
            var ok = result switch
            {
                ActionResult a => a.Ok,
                CommitResult c => c.Ok,
                _ => false,
            };
            Assert.True(ok, $"{id}: the action failed");
            CheckAllThree(id, folder);
        }

        public void CheckAllThree(string id, CardFolder folder)
        {
            Check(id, folder, "right after the action");
            CheckAfterTheTick(id, folder);
        }

        /// <summary>
        /// For a commit the page did not make (a planner batch, a worker's move): the page has it from the tick's
        /// next card read on, and keeps it once the overlay's keep has passed.
        /// </summary>
        public void CheckAfterTheTick(string id, CardFolder folder)
        {
            var reads = _host.Reads;
            _clock.Advance(TimeSpan.FromSeconds(20));
            Tick.Run();
            Assert.True(_host.Reads > reads, "the tick did not read the cards");
            Check(id, folder, "after the next tick");
            _clock.Advance(PageCardOverlay.Keep + TimeSpan.FromSeconds(1));
            Check(id, folder, "after the overlay's keep");
        }

        /// <summary>The one card the page's list holds for this id (the overlay laid over the tick's cards, as the snapshot builds it).</summary>
        public Card Shown(string id) => Assert.Single(_overlay.Apply(Tick.Cards), c => c.Id == id);

        public string MainText(string path) => GitFixture.Git(_fx.Bare, "show", $"main:{path}").Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

        private void Check(string id, CardFolder folder, string when)
        {
            // Main, read with git from the remote itself.
            var paths = GitFixture.Git(_fx.Bare, "ls-tree", "-r", "--name-only", "main", Jobs)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => Path.GetFileName(p).StartsWith(id + "-", StringComparison.Ordinal))
                .ToList();
            var path = Assert.Single(paths);
            var onMain = CardFiles.Parse(Jobs, path, MainText(path))!;
            Assert.Equal(folder, onMain.Folder);

            // Page snapshot will be built from this list.
            var shown = _overlay.Apply(Tick.Cards).Where(c => c.Id == id).ToList();
            Assert.True(shown.Count == 1, $"{id} {when}: the page has {shown.Count} rows for it ({string.Join(", ", shown.Select(c => c.Path))}); main has {path}");
            Assert.True(shown[0].Path == path, $"{id} {when}: the page has it at {shown[0].Path}; main has {path}");
            Assert.True(shown[0].Status == onMain.Status, $"{id} {when}: the page says '{shown[0].Status}'; main says '{onMain.Status}'");
            Assert.Equal(onMain.Questions.Select(q => q.Number), shown[0].Questions.Select(q => q.Number));

            // And the rows the page draws: never twice, none for a finished card, one queue row for a queued one.
            var snapshot = HostPageSnapshot.Build(_config, _clock, Tick, _sources);
            var name = Path.GetFileNameWithoutExtension(path);
            var rows = snapshot.Waiting.Count(w => w.Name == name) + snapshot.Queue.Count(q => q.Name == name);
            Assert.True(rows <= 1, $"{id} {when}: the page draws {rows} rows for it: waiting [{string.Join(", ", snapshot.Waiting.Where(w => w.Name == name).Select(w => $"{w.Kind} {w.Note}"))}], queue {snapshot.Queue.Count(q => q.Name == name)}, started {_host.Starts.Count}");
            if (folder == CardFolder.Done)
            {
                Assert.Equal(0, rows);
            }

            if (folder == CardFolder.Queue)
            {
                Assert.Equal(1, snapshot.Queue.Count(q => q.Name == name));
            }
        }
    }
}
