using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Core.Moves;
using Scheduler.Core.Tests.Health;

namespace Scheduler.Core.Tests.Moves;

/// <summary>
/// Resume / Send back / Ready from the page (MOVE-7, Resume-Card L1530) and the retry counts the
/// page clears (MOVE-8, L3950-3953 and L3855). Test plan: scheduler-next/test-plans/claims-and-moves.md.
/// </summary>
public class MovesResumeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 11, 20, 0, TimeSpan.Zero);

    private static Dictionary<string, int> Retries() =>
        new(StringComparer.OrdinalIgnoreCase) { ["010-A"] = 2, ["020-B"] = 2 };

    // MOVE-7.
    [Theory]
    [InlineData("review", "review: check it", CardFolder.Active, "sent back from review from the page; read any ## Dev section first.", "jobs: sent back 010-A-x (from the page)", true)]
    [InlineData("active", "needs-human: which?", CardFolder.Active, "resumed from the page; read any ## Dev section first.", "jobs: resumed 010-A-x (from the page)", true)]
    [InlineData("active", "waiting: developer", CardFolder.Active, "resumed from the page; read any ## Dev section first.", "jobs: resumed 010-A-x (from the page)", true)]
    [InlineData("queue", "waiting: you", CardFolder.Queue, "set ready from the page; read any ## Dev section first.", "jobs: ready 010-A-x (from the page)", false)]
    [InlineData("queue", "needs-human: which?", CardFolder.Queue, "resumed from the page; read any ## Dev section first.", "jobs: resumed 010-A-x (from the page)", true)]
    public void Moves_ResumeCard_EachSource(string from, string status, CardFolder folder, string journal, string message, bool clears)
    {
        var text = $"# T\nStatus: {status}\n  more\nLocks: none\n\n## Result\n";
        var retries = Retries();

        var edit = CardMoves.Resume(MovesAnswerTests.Card(from, text), text, Now);
        CardMoves.Committed(edit, retries);

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(folder, edit.Folder);
        Assert.Equal("ready", edit.Status);
        Assert.Equal($"# T\nStatus: ready\nLocks: none\n\n## Result\n\n- 2026-11-18 11:20 developer: {journal}\n", edit.Text);
        Assert.Equal(message, edit.Message);
        Assert.Equal(clears, edit.ClearsRetries);
        Assert.Equal(!clears, retries.ContainsKey("010-A"));
        Assert.Equal(2, retries["020-B"]);
    }

    // MOVE-7: only cards waiting on the developer resume; nothing else changes.
    [Theory]
    [InlineData("active", "ready")]
    [InlineData("queue", "ready")]
    [InlineData("done", "done")]
    [InlineData("active", "waiting: planner (request #3)")]
    public void Moves_ResumeCard_EachSource_NotWaitingIsError(string from, string status)
    {
        var text = $"Status: {status}\n";
        var retries = Retries();

        var edit = CardMoves.Resume(MovesAnswerTests.Card(from, text), text, Now);
        CardMoves.Committed(edit, retries);

        Assert.False(edit.Ok);
        Assert.Equal("010-A is not waiting on you", edit.Error);
        Assert.Equal(2, retries["010-A"]);
    }

    // MOVE-7, MOVE-2.
    // Spec C9: today's behaviour, kept until the developer decides (003-D review).
    [Fact]
    public void Moves_ResumeQueueCard_RetriesNotCleared()
    {
        var queued = "Status: waiting: you\n";
        var asked = "Status: needs-human: x\n## Questions\n- Q1: Ok? [yes -> active | no -> done]\n";
        var retries = Retries();

        var resume = CardMoves.Resume(MovesAnswerTests.Card("queue", queued), queued, Now);
        CardMoves.Committed(resume, retries);
        var answer = CardMoves.Answer(MovesAnswerTests.Card("review", asked), asked, 1, 0, "", Now);
        CardMoves.Committed(answer, retries);

        Assert.True(resume.Ok, resume.Error);
        Assert.True(answer.Ok, answer.Error);
        Assert.Equal(CardFolder.Active, answer.Folder);
        Assert.Equal(2, retries["010-A"]);
    }

    // MOVE-7 as 1149-B extended it: a needs-human card parked in queue/ is waiting on the developer
    // too (the dispatcher never takes it), so Resume sets it ready where it stands and clears its
    // retries as for an active card. It stays in queue/ - it was never claimed.
    [Fact]
    public void Moves_ResumeQueuedNeedsHuman_StaysInQueueReadyAndClearsRetries()
    {
        var text = "# T\nStatus: needs-human: leave it, or remove its worktree?\n  more\nLocks: none\n\n## Result\n";
        var retries = Retries();

        var edit = CardMoves.Resume(MovesAnswerTests.Card("queue", text), text, Now);
        CardMoves.Committed(edit, retries);

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Queue, edit.Folder);
        Assert.Equal("ready", edit.Status);
        Assert.Equal($"# T\nStatus: ready\nLocks: none\n\n## Result\n\n- 2026-11-18 11:20 developer: resumed from the page; read any ## Dev section first.\n", edit.Text);
        Assert.Equal("jobs: resumed 010-A-x (from the page)", edit.Message);
        Assert.True(edit.ClearsRetries);
        Assert.False(retries.ContainsKey("010-A"));
        Assert.Equal(2, retries["020-B"]);
    }

    // MOVE-8: Retry removes the card's entry; Resume from paused clears them all (HealthCheck.Resume).
    [Fact]
    public void Moves_RetryAndResumeState_ClearRetries()
    {
        var retries = Retries();
        var health = new HealthCheck(new SchedulerConfig(), new FakeClock(), new FakeLog(), new FakeHealthRunner("abc1234"));

        Assert.True(CardMoves.Retry(retries, "010-A"));
        Assert.Equal(new Dictionary<string, int> { ["020-B"] = 2 }, retries);
        Assert.False(CardMoves.Retry(retries, "030-C"));

        Assert.Equal(SchedulerState.Running, health.Resume(SchedulerState.Paused, retries));
        Assert.Empty(retries);
    }
}
