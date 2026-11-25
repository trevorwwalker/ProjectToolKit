// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.PageActions;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Program;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 990-B (the developer, 2026-11-20 09:03: 985-D answered and 984-D Done vanished, then came back
/// for a while). The page action had landed, but the tick's card list still held each card where it
/// was until its next card read, and the page was rebuilt from that list. Now the scheduler's own
/// view moves with the action: the committed move is laid over the tick's list (<see cref="PageCardOverlay"/>)
/// so the next snapshot - before any card read - shows the card where it went, until the tick's
/// own read agrees.
/// </summary>
public sealed class PageCardOverlayTests : IDisposable
{
    private const string Jobs = "docs/jobs";
    private const string ReviewPath = Jobs + "/review/984-D-sim-check.md";
    private const string ActivePath = Jobs + "/active/985-D-two-questions.md";
    private const string ReviewText = "# 984-D: Sim check\n\nCategory: D\nStatus: review: look at it\n\n## Questions\n\n## Result\n\n- checked\n";
    private const string ActiveText =
        "# 985-D: Two questions\n\nCategory: D\nStatus: needs-human: two questions\n\n## Questions\n\n" +
        "- Q1: Found: a. Question: keep it? [yes | no]\n" +
        "- Q2: Found: b. Question: finish? [finish -> done | leave it]\n\n## Result\n\n- found two things\n";

    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 20, 9, 3, 0, TimeSpan.Zero));
    private readonly PageReviewRefreshTests.InMemoryMain _main = new();
    private readonly ActionTickHost _host = new();
    private readonly RecordingCommitter _committer = new();
    private readonly SchedulerConfig _config;
    private readonly SchedulerTick _tick;
    private readonly PageCardOverlay _overlay;
    private readonly PageSnapshotSources _sources;
    private readonly HostPageActions _actions;

    public PageCardOverlayTests()
    {
        var logDir = Path.Combine(_temp.Path, "logs");
        Directory.CreateDirectory(logDir);
        _config = new SchedulerConfig
        {
            Project = "ptk-990b",
            JobsDir = Jobs,
            LogDir = logDir,
            Repo = _temp.Path,
            WorkspaceRoot = Path.Combine(_temp.Path, "ws"),
            Slots = 2,
            PageStopGraceMinutes = 5,
            PollSeconds = 1,
            Agents = new Dictionary<string, AgentDefinition>(),
        };
        var files = new StateFiles(logDir);
        var log = new ListLog();
        _main.Files[ReviewPath] = ReviewText;
        _main.Files[ActivePath] = ActiveText;
        _host.Cards.Add(CardFiles.Parse(Jobs, ReviewPath, ReviewText)!);
        _host.Cards.Add(CardFiles.Parse(Jobs, ActivePath, ActiveText)!);
        _tick = new SchedulerTick(_config, _clock, log, new TickLoad(), new TickProbe(), new ActionProcesses(), files, _host);
        _tick.Run();

        _overlay = new PageCardOverlay(Jobs, _clock);
        _sources = new PageSnapshotSources { Overlay = _overlay };
        _actions = new HostPageActions(new PageActionParts
        {
            Config = _config,
            Clock = _clock,
            Log = log,
            Files = files,
            Tick = _tick,
            Processes = new ActionProcesses(),
            Committer = _committer,
            Main = _main,
            // No tick runs: what the snapshot shows next comes from the overlay alone.
            TickNow = () => { },
            Overlay = _overlay,
        });
    }

    public void Dispose() => _temp.Dispose();

    private IReadOnlyList<string> Waiting() =>
        [.. HostPageSnapshot.Build(_config, _clock, _tick, _sources).Waiting.Select(w => w.Name)];

    [Fact]
    public void Done_TheNextSnapshotBeforeAnyCardRead_NoLongerShowsTheCardWaiting()
    {
        Assert.Contains("984-D-sim-check", Waiting());
        var reads = _host.Reads;

        Assert.True(_actions.Done("984-D-sim-check").Ok);

        Assert.Equal(reads, _host.Reads);
        Assert.Contains(_tick.Cards, c => c.Path == ReviewPath);   // the tick still has it in review/
        Assert.DoesNotContain("984-D-sim-check", Waiting());
        var moved = Assert.Single(_overlay.Apply(_tick.Cards), c => c.Id == "984-D");
        Assert.Equal(CardFolder.Done, moved.Folder);
    }

    [Fact]
    public void AnswerThatMovesTheCard_TheNextSnapshotShowsItInItsNewFolder()
    {
        Assert.Contains("985-D-two-questions", Waiting());

        Assert.True(_actions.Answer("985-D-two-questions", 1, "0", "").Ok);
        // Q1 answered: the card stays, with only Q2 open.
        var still = Assert.Single(_overlay.Apply(_tick.Cards), c => c.Id == "985-D");
        Assert.Equal([2], still.Questions.Select(q => q.Number));

        // Q2's "finish -> done", answered from the text the first answer committed.
        _main.Files[ActivePath] = _committer.Changes[^1].Writes[ActivePath];
        Assert.True(_actions.Answer("985-D-two-questions", 2, "0", "").Ok);

        Assert.Equal(0, _host.Reads - 1);   // only the constructor's tick read the cards
        Assert.DoesNotContain("985-D-two-questions", Waiting());
        var moved = Assert.Single(_overlay.Apply(_tick.Cards), c => c.Id == "985-D");
        Assert.NotEqual(CardFolder.Active, moved.Folder);
        Assert.NotEqual(ActivePath, moved.Path);
    }

    [Fact]
    public void Overlay_IsForgottenOnceTheTicksOwnReadAgrees()
    {
        Assert.True(_actions.Done("984-D-sim-check").Ok);
        Assert.Equal(1, _overlay.Count);

        // The tick reads main and now has the card in done/.
        var change = _committer.Changes[^1];
        var to = change.Moves[0].To;
        _host.Cards.RemoveAll(c => c.Path == ReviewPath);
        _host.Cards.Add(CardFiles.Parse(Jobs, to, change.Writes[to])!);
        _tick.Run();

        Assert.DoesNotContain("984-D-sim-check", Waiting());
        Assert.Equal(0, _overlay.Count);
    }

    [Fact]
    public void Overlay_ExpiresWhenMainNeverShowsTheMove()
    {
        Assert.True(_actions.Done("984-D-sim-check").Ok);
        Assert.DoesNotContain("984-D-sim-check", Waiting());

        _clock.Advance(PageCardOverlay.Keep + TimeSpan.FromSeconds(1));

        Assert.Contains("984-D-sim-check", Waiting());
        Assert.Equal(0, _overlay.Count);
    }

    [Fact]
    public void FailedCommit_LeavesTheCardWhereItWas()
    {
        _committer.FailWith = "push refused";

        Assert.False(_actions.Done("984-D-sim-check").Ok);

        Assert.Equal(0, _overlay.Count);
        Assert.Contains("984-D-sim-check", Waiting());
    }
}
