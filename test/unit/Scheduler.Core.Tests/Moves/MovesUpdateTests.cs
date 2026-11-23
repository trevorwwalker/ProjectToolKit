using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;

namespace Scheduler.Core.Tests.Moves;

/// <summary>
/// Update-Card's text (L1204-1254): the Status line replaced with its continuations dropped
/// (MOVE-3), the developer's journal line at the end (MOVE-5), and Done for review cards only
/// (MOVE-6, Done-Card L1521). Test plan: scheduler-next/test-plans/claims-and-moves.md.
/// </summary>
public class MovesUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 11, 20, 0, TimeSpan.Zero);

    // MOVE-3.
    [Fact]
    public void Moves_SetStatus_ReplacesFirstAndDropsContinuations()
    {
        Assert.Equal("Status: ready\nLocks: none", CardText.SetStatus("Status: needs-human: a\n  b\n  c\nLocks: none", "ready"));
        Assert.Equal("# T\r\nStatus: ready\r\n\r\nStatus: x\r\n  y", CardText.SetStatus("# T\r\nStatus: old\r\n  more\r\n\r\nStatus: x\r\n  y", "ready"));
        Assert.Equal("Lane: A\n Status: x", CardText.SetStatus("Lane: A\n Status: x", "ready"));
    }

    // MOVE-5.
    [Fact]
    public void Moves_JournalLine_AppendedAtEnd()
    {
        Assert.Equal(
            "# T\n## Result\n\n- old\n\n- 2026-11-18 11:20 developer: resumed\n",
            CardText.AddJournal("# T\n## Result\n\n- old\n\n  \n\n", "resumed", Now));
        Assert.Equal(
            "# T\r\n- old\r\n\r\n- 2026-11-18 11:20 developer: x\r\n",
            CardText.AddJournal("# T\r\n- old", "x", Now));
    }

    // MOVE-3, MOVE-5: Update-Card sets the Status, adds the journal line and moves the card.
    [Fact]
    public void Moves_JournalLine_AppendedAtEnd_ViaUpdate()
    {
        var text = "# T\nStatus: review: look\n  here\nLocks: none\n\n## Result\n\n- old\n";
        var card = MovesAnswerTests.Card("review", text);

        var edit = CardMoves.Update(card, text, "ready", CardFolder.Active, "a line", "jobs: m", Now);

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal("# T\nStatus: ready\nLocks: none\n\n## Result\n\n- old\n\n- 2026-11-18 11:20 developer: a line\n", edit.Text);
        Assert.Equal("docs/jobs/active/010-A-x.md", edit.NewPath);
        Assert.Equal("jobs: m", edit.Message);
        Assert.Equal("ready", edit.Status);
    }

    // MOVE-6.
    [Fact]
    public void Moves_Done_ReviewCardsOnly()
    {
        var text = "# T\nStatus: review: check the page\nLocks: none\n\n## Result\n\n- worked\n";

        var review = CardMoves.Done(MovesAnswerTests.Card("review", text), text, Now);
        var active = CardMoves.Done(MovesAnswerTests.Card("active", text), text, Now);

        Assert.True(review.Ok, review.Error);
        Assert.Equal("done", review.Status);
        Assert.Equal(CardFolder.Done, review.Folder);
        var move = Assert.Single(review.Change.Moves);
        Assert.Equal("docs/jobs/review/010-A-x.md", move.From);
        Assert.Equal("docs/jobs/done/010-A-x.md", move.To);
        Assert.Equal("# T\nStatus: done\nLocks: none\n\n## Result\n\n- worked\n\n- 2026-11-18 11:20 developer: reviewed, done (from the page).\n", review.Text);
        Assert.Equal("jobs: done 010-A-x (reviewed from the page)", review.Message);
        Assert.False(review.ClearsRetries);

        Assert.False(active.Ok);
        Assert.Equal("010-A is not in review", active.Error);
        Assert.Equal("", active.Text);
    }
}
