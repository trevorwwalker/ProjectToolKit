// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;

namespace Scheduler.Core.Tests.Moves;

/// <summary>
/// Answering a card's question from the page (MOVE-2, Answer-Card L1076), with the developer's
/// decision of 2026-11-18 (card 200-A): the answer is recorded as it is given, but the card moves
/// and its Status changes only when its last open question is answered, to the destination needing
/// the most work among the chosen answers (active over queue over done). Test plan:
/// scheduler-next/test-plans/claims-and-moves.md.
/// </summary>
public class MovesAnswerTests
{
    private const string Jobs = "docs/jobs";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 11, 20, 0, TimeSpan.Zero);

    // Choices of every question: 0 -> active, 1 -> done, 2 -> queue, 3 no destination.
    private const string TwoQuestions =
        "# 010-A: X\n\nLane: A\nStatus: needs-human: two things\n  more words\nLocks: none\n\n" +
        "## Questions\n\n" +
        "- Q1: First? [go on -> active | finish -> done | back -> queue | wait]\n" +
        "- Q2: Second? [go on -> active | finish -> done | back -> queue | wait]\n\n" +
        "## Result\n\n- old line\n";

    internal static Card Card(string folder, string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folder}/010-A-x.md", text)
        ?? throw new InvalidOperationException("not a card path");

    private static Card Reread(CardEdit edit) =>
        CardFiles.Parse(Jobs, edit.NewPath, edit.Text) ?? throw new InvalidOperationException("not a card path");

    private static string[] Lines(string text) => text.Split('\n');

    // MOVE-2.
    [Fact]
    public void Moves_Answer_LineAndNote()
    {
        var text = "# 010-A: X\nStatus: needs-human: ok?\n\n## Questions\n\n- Q1: Ok? [yes -> done | no | later]\n\n## Result\n";
        var note = new string('n', 600);

        var edit = CardMoves.Answer(Card("active", text), text, 1, 1, note, Now);

        Assert.True(edit.Ok, edit.Error);
        var lines = Lines(edit.Text);
        var at = Array.FindIndex(lines, l => l.StartsWith("- Q1:", StringComparison.Ordinal));
        Assert.EndsWith(" -> **no** (developer, 2026-11-18 11:20)", lines[at], StringComparison.Ordinal);
        Assert.Equal("- Q1: Ok? [yes -> done | no | later] -> **no** (developer, 2026-11-18 11:20)", lines[at]);
        Assert.Equal("  Note: " + new string('n', 500), lines[at + 1]);
        Assert.Equal("jobs: answer 010-A Q1: no", edit.Message);
    }

    // MOVE-2: the note's runs of white space become one space, and no note means no note line.
    [Fact]
    public void Moves_Answer_LineAndNote_NoteSpacesAndNoNote()
    {
        var text = "Status: x\n## Questions\n- Q1: Ok? [yes | no]\n- Q2: Ok? [yes | no]\n";

        var spaced = CardMoves.Answer(Card("active", text), text, 1, 0, "  a \t\r\n b  ", Now);
        var none = CardMoves.Answer(Card("active", text), text, 1, 0, "", Now);

        Assert.Equal("  Note: a b", Lines(spaced.Text)[3]);
        Assert.StartsWith("- Q2:", Lines(none.Text)[3], StringComparison.Ordinal);
    }

    // MOVE-2 (developer decision 2026-11-18): Q1 and Q2 open; answering Q1 with a -> active choice
    // records it and leaves the card where it is with its Status; answering Q2 then moves it.
    [Fact]
    public void Moves_Answer_DestinationWaitsForLastOpenQuestion()
    {
        var text = TwoQuestions.Replace("needs-human: two things", "review: check", StringComparison.Ordinal);

        var first = CardMoves.Answer(Card("review", text), text, 1, 0, "", Now);

        Assert.True(first.Ok, first.Error);
        Assert.False(first.Moves);
        Assert.Equal(CardFolder.Review, first.Folder);
        Assert.Equal("", first.Status);
        Assert.Contains("\nStatus: review: check\n  more words\n", first.Text, StringComparison.Ordinal);
        Assert.Contains("- Q1: First? [go on -> active | finish -> done | back -> queue | wait] -> **go on** (developer, 2026-11-18 11:20)", first.Text, StringComparison.Ordinal);
        Assert.Equal("jobs: answer 010-A Q1: go on", first.Message);
        Assert.Empty(first.Change.Moves);

        var second = CardMoves.Answer(Reread(first), first.Text, 2, 3, "", Now);

        Assert.True(second.Ok, second.Error);
        Assert.True(second.Moves);
        Assert.Equal(CardFolder.Active, second.Folder);
        Assert.Equal($"{Jobs}/active/010-A-x.md", second.NewPath);
        Assert.Equal("ready", second.Status);
        Assert.Contains("\nStatus: ready\nLocks: none\n", second.Text, StringComparison.Ordinal);
        Assert.Equal("jobs: answer 010-A Q2: wait - to active", second.Message);
        var move = Assert.Single(second.Change.Moves);
        Assert.Equal($"{Jobs}/review/010-A-x.md", move.From);
        Assert.Equal($"{Jobs}/active/010-A-x.md", move.To);
        Assert.Equal(second.Text, second.Change.Writes[$"{Jobs}/active/010-A-x.md"]);
    }

    // MOVE-2 (developer decision 2026-11-18): the last answer sends the card to the destination
    // needing the most work among the chosen answers: active over queue over done.
    [Theory]
    [InlineData(0, 1, CardFolder.Active, "ready", "active")]
    [InlineData(1, 0, CardFolder.Active, "ready", "active")]
    [InlineData(2, 1, CardFolder.Queue, "ready", "queue")]
    [InlineData(1, 2, CardFolder.Queue, "ready", "queue")]
    [InlineData(1, 1, CardFolder.Done, "done", "done")]
    public void Moves_Answer_SeveralDestinations_MostWorkWins(int q1, int q2, CardFolder folder, string status, string dir)
    {
        var text = TwoQuestions.Replace("needs-human: two things", "review: check", StringComparison.Ordinal);

        var first = CardMoves.Answer(Card("review", text), text, 1, q1, "", Now);
        var second = CardMoves.Answer(Reread(first), first.Text, 2, q2, "", Now);

        Assert.False(first.Moves);
        Assert.Equal(folder, second.Folder);
        Assert.Equal($"{Jobs}/{dir}/010-A-x.md", second.NewPath);
        Assert.Equal(status, second.Status);
        Assert.Contains($"\nStatus: {status}\nLocks: none\n", second.Text, StringComparison.Ordinal);
        Assert.EndsWith($" - to {dir}", second.Message, StringComparison.Ordinal);
    }

    // MOVE-2: a single question with a destination moves the card when answered (it is the last),
    // and a destination that is the card's own folder sets ready without a move.
    [Theory]
    [InlineData("active", 1, CardFolder.Done, "done", "jobs: answer 010-A Q1: finish - to done")]
    [InlineData("active", 2, CardFolder.Queue, "ready", "jobs: answer 010-A Q1: back - to queue")]
    [InlineData("review", 0, CardFolder.Active, "ready", "jobs: answer 010-A Q1: go on - to active")]
    [InlineData("active", 0, CardFolder.Active, "ready", "jobs: answer 010-A Q1: go on")]
    public void Moves_Answer_DestinationWaitsForLastOpenQuestion_SingleQuestion(string from, int choice, CardFolder folder, string status, string message)
    {
        var text = "Status: x\n\n## Questions\n\n- Q1: First? [go on -> active | finish -> done | back -> queue | wait]\n";

        var edit = CardMoves.Answer(Card(from, text), text, 1, choice, "", Now);

        Assert.Equal(folder, edit.Folder);
        Assert.Equal(status, edit.Status);
        Assert.StartsWith($"Status: {status}\n", edit.Text, StringComparison.Ordinal);
        Assert.Equal(message, edit.Message);
    }

    // MOVE-2: no destination; the card's last open question answered makes an active card ready,
    // and it stays in active/.
    [Fact]
    public void Moves_Answer_LastOpenQuestion_ActiveBecomesReady()
    {
        var first = CardMoves.Answer(Card("active", TwoQuestions), TwoQuestions, 1, 3, "", Now);

        Assert.True(first.Ok, first.Error);
        Assert.Equal("", first.Status);
        Assert.Contains("\nStatus: needs-human: two things\n  more words\n", first.Text, StringComparison.Ordinal);

        var second = CardMoves.Answer(Reread(first), first.Text, 2, 3, "", Now);

        Assert.True(second.Ok, second.Error);
        Assert.Equal("ready", second.Status);
        Assert.Contains("\nStatus: ready\nLocks: none\n", second.Text, StringComparison.Ordinal);
        Assert.False(second.Moves);
        Assert.Equal(CardFolder.Active, second.Folder);
        Assert.Equal("jobs: answer 010-A Q2: wait", second.Message);
    }

    // MOVE-2: no destination on a review card leaves its Status alone even on the last answer.
    [Fact]
    public void Moves_Answer_LastOpenQuestion_ReviewCardUnchanged()
    {
        var text = "Status: review: look\n## Questions\n- Q1: Ok? [yes | no]\n";

        var edit = CardMoves.Answer(Card("review", text), text, 1, 0, "", Now);

        Assert.Equal("", edit.Status);
        Assert.StartsWith("Status: review: look\n", edit.Text, StringComparison.Ordinal);
        Assert.False(edit.Moves);
    }

    // MOVE-2 (820-A): a question written over several lines is answered on its last line - the one
    // the closing bracket is on - and the note goes on the line after it.
    [Fact]
    public void Moves_Answer_WrappedQuestion_AnswerGoesOnItsLastLine()
    {
        var text = string.Join('\n',
            "# 010-A: X",
            "Status: needs-human: ok?",
            "",
            "## Questions",
            "",
            "- Q1: Found: the answers the developer gave on 760-A were lost because the",
            "  question was written over three lines and only the first was read, so the",
            "  page refused the click and said nothing. Question: read the whole line?",
            "  [yes, read them all -> done | no, leave it as it is -> queue | later]",
            "",
            "- Q2: Single? [go on -> active | wait]",
            "",
            "## Result",
            "");

        var edit = CardMoves.Answer(Card("active", text), text, 1, 0, "read them all", Now);

        Assert.True(edit.Ok, edit.Error);
        var lines = Lines(edit.Text);
        var at = Array.FindIndex(lines, l => l.StartsWith("  [yes, read them all", StringComparison.Ordinal));
        Assert.Equal("  [yes, read them all -> done | no, leave it as it is -> queue | later] -> **yes, read them all** (developer, 2026-11-18 11:20)", lines[at]);
        Assert.Equal("  Note: read them all", lines[at + 1]);

        // The first two lines of the question are untouched, and the question the card holds after
        // the answer is the whole block, not its first line.
        Assert.Equal("- Q1: Found: the answers the developer gave on 760-A were lost because the", lines[5]);
        var asked = Assert.Single(Reread(edit).Questions);
        Assert.Equal(2, asked.Number);
        Assert.Equal("Single?", asked.Text);
    }

    // MOVE-2 (820-A): a wrapped question that already has its answer is refused as answered, whether
    // the page still shows it open or not - the same two refusals a single-line question gets.
    [Fact]
    public void Moves_Answer_WrappedQuestion_AlreadyAnswered_IsError()
    {
        var open = "- Q1: Found: it is\n  broken. Question: fix?\n  [yes -> done | no -> queue]\n";
        var answered = "- Q1: Found: it is\n  broken. Question: fix?\n  [yes -> done | no -> queue] -> **yes** (developer, 2026-11-18 11:00)\n";
        var head = "Status: x\n## Questions\n";

        var stale = CardMoves.Answer(Card("active", head + open), head + answered, 1, 0, "", Now);
        var known = CardMoves.Answer(Card("active", head + answered), head + answered, 1, 0, "", Now);

        Assert.False(stale.Ok);
        Assert.Equal("Q1 on 010-A is already answered", stale.Error);
        Assert.False(known.Ok);
        Assert.Equal("no open question Q1 on 010-A", known.Error);
    }

    // MOVE-2 (820-A): the open count and the destinations read the wrapped question the same way,
    // so the card moves on its last open question with the destination the wrapped answer chose.
    [Fact]
    public void Moves_Answer_WrappedQuestion_CountedAndMovedWithTheSameJoining()
    {
        var text = string.Join('\n',
            "Status: needs-human: two",
            "",
            "## Questions",
            "",
            "- Q1: Found: the first answer went missing when the question",
            "  was written over more than one line. Question: how does the card go?",
            "  [back -> queue | stay]",
            "  -> **back** (developer, 2026-11-18 11:00)",
            "  Note: back to the queue",
            "",
            "- Q2: Second? [wait | later]",
            "",
            "## Result",
            "");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 0, "", Now);

        // Q1's answer is found across its block (its destination is queue) and Q2 was the last open
        // question, so the card goes to queue/ ready.
        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Queue, edit.Folder);
        Assert.Equal("ready", edit.Status);
        Assert.Equal("jobs: answer 010-A Q2: wait - to queue", edit.Message);
    }

    // MOVE-2: a single-line question answers exactly as it always did, next to a wrapped one.
    [Fact]
    public void Moves_Answer_WrappedQuestion_SingleLineUnchanged()
    {
        var text = "Status: x\n## Questions\n- Q1: Ok? [yes -> done | no | later]\n";

        var edit = CardMoves.Answer(Card("active", text), text, 1, 1, "", Now);

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal("- Q1: Ok? [yes -> done | no | later] -> **no** (developer, 2026-11-18 11:20)", Lines(edit.Text)[2]);
    }

    // MOVE-2 (948-A): only the answers given since the card last began to wait count. Q1 was
    // answered "-> active" and acted on in an earlier round; the developer then answered Q2
    // "-> done", and the card must go to done/, not back to active/ (935-A on 2026-11-19).
    [Fact]
    public void Moves_Answer_EarlierRoundAnswer_NotCounted()
    {
        var text = Waited(
            "- Q1: First? [go on -> active | finish -> done] -> **go on** (developer, 2026-11-19 12:20)",
            "- Q2: Second? [go on -> active | finish -> done]",
            "- 2026-11-19 12:16: Parked with Q1 and Q2." + Line(JournalTime(12, 41), "Resumed, worked, and parked again with Q2."));

        var edit = CardMoves.Answer(Card("active", text), text, 2, 1, "", At(13, 37));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Done, edit.Folder);
        Assert.Equal("done", edit.Status);
        Assert.Equal("jobs: answer 010-A Q2: finish - to done", edit.Message);
    }

    // MOVE-2 (948-A): the rule the developer kept - two questions answered in the same round move
    // the card to the destination needing the most work, active over queue over done.
    [Fact]
    public void Moves_Answer_BothThisRound_MostWorkWins()
    {
        var text = Waited(
            "- Q1: First? [go on -> active | finish -> done] -> **finish** (developer, 2026-11-19 13:30)",
            "- Q2: Second? [go on -> active | finish -> done]",
            "- 2026-11-19 12:41: Parked with Q1 and Q2.");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 0, "", At(13, 37));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Active, edit.Folder);
        Assert.Equal("ready", edit.Status);
    }

    // MOVE-2 (948-A): a card whose Result carries no dated line has no round to start, so every
    // answer counts as it did before 948-A.
    [Fact]
    public void Moves_Answer_NoDatedJournalEntry_EveryAnswerCounts()
    {
        var text = Waited(
            "- Q1: First? [go on -> active | finish -> done] -> **go on** (developer, 2026-11-19 12:20)",
            "- Q2: Second? [go on -> active | finish -> done]",
            "- old line, never dated");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 1, "", At(13, 37));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Active, edit.Folder);
    }

    // MOVE-2 (948-A): a journal line dated without a time still starts the round; an answer given
    // that same day is later than it and counts.
    [Fact]
    public void Moves_Answer_JournalEntryWithoutTime_SameDayAnswersCount()
    {
        var text = Waited(
            "- Q1: First? [go on -> active | finish -> done] -> **go on** (developer, 2026-11-19 12:20)",
            "- Q2: Second? [go on -> active | finish -> done]",
            "- 2026-11-19: Parked again with Q1 and Q2.");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 1, "", At(13, 37));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Active, edit.Folder);
    }

    // MOVE-2 (948-A): the round starts when a worker hands the card over, not when the page writes
    // a journal line of its own (a "- <date> developer: ..." line from CardText.AddJournal, which
    // marks the card leaving a waiting state), so answers given around it stay in one round.
    [Fact]
    public void Moves_Answer_PageJournalLine_DoesNotStartARound()
    {
        var text = Waited(
            "- Q1: First? [go on -> active | finish -> done] -> **go on** (developer, 2026-11-19 12:45)",
            "- Q2: Second? [go on -> active | finish -> done]",
            "- 2026-11-19 12:41: Parked with Q1 and Q2." +
                "\n- 2026-11-19 12:50 developer: sent back from review from the page; read any ## Dev section first." +
                "\n- 2026-11-19 12:52: developer: the same line with a colon after its time.");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 1, "", At(13, 0));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Active, edit.Folder);
    }

    // MOVE-2 (948-A): with every destination of this round dropped, the last open question on an
    // active card leaves it ready where it is, as MOVE-2 says for a card with no destination.
    [Fact]
    public void Moves_Answer_OnlyEarlierRoundDestinations_ReadyWhereItIs()
    {
        var text = Waited(
            "- Q1: First? [go on -> active | finish -> done] -> **finish** (developer, 2026-11-19 12:20)",
            "- Q2: Second? [wait | later]",
            "- 2026-11-19 12:41: Parked with Q1 and Q2.");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 0, "", At(13, 37));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Active, edit.Folder);
        Assert.Equal("ready", edit.Status);
    }

    // 1057-B (1033-C, the developer 2026-11-21 22:50): Q1 was answered "-> queue" and acted on in an
    // earlier round, and its stamp carries the developer's note inside the parentheses, so the
    // closing bracket does not follow the clock. Q2 was answered "-> done" and Q3 - the last open
    // question - is answered "-> done" now. The old queue must not outrank the new done: the card
    // goes to done/ with Status done.
    [Fact]
    public void Moves_Answer_EarlierRoundAnswerWithNoteInStamp_NotCounted()
    {
        var text = ThreeQuestions(
            "- Q1: Found: the box saves. Question: does the number stay? [yes -> done | no, send it back -> queue] -> **no, send it back** (developer, 2026-11-21 19:40: \"I set 8 and 4, they went back to 6\")",
            "- Q2: Found: the saves land. Question: do they stay now? [yes, they stay -> done | no, they still reset -> queue] -> **yes, they stay** (developer, 2026-11-21 22:49)",
            "- Q3: Found: a quick second change is no longer dropped. Question: does 6 stay at 6? [yes -> done | no, it still resets -> queue]");

        var edit = CardMoves.Answer(Card("active", text), text, 3, 0, "", On(22, 50));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Done, edit.Folder);
        Assert.Equal("done", edit.Status);
        Assert.Equal("jobs: answer 010-A Q3: yes - to done", edit.Message);
    }

    // 1057-B: an answer written this round with its note inside the parentheses still counts, so two
    // same-round answers still go to the destination needing the most work (the 948-A rule, kept).
    [Fact]
    public void Moves_Answer_SameRoundAnswerWithNoteInStamp_StillCounts()
    {
        var text = ThreeQuestions(
            "- Q1: Found: the box saves. Question: keep going? [go on -> active | finish -> done | back -> queue] -> **go on** (developer, 2026-11-21 22:20: \"one more look\")",
            "- Q2: Found: the saves land. Question: send it back? [go on -> active | finish -> done | back -> queue] -> **back** (developer, 2026-11-21 22:45)",
            "- Q3: Found: a quick second change is no longer dropped. Question: does 6 stay at 6? [go on -> active | finish -> done | back -> queue]");

        var edit = CardMoves.Answer(Card("active", text), text, 3, 1, "", On(22, 50));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Active, edit.Folder);
    }

    // 1057-B: the destination is that of the question just answered. The page's copy of the card can
    // be a minute behind the journal line the worker pushed, so the fresh choice counts even when its
    // stamp sorts before the round's start; without it the card would sit where it is.
    [Fact]
    public void Moves_Answer_FreshChoice_PageClockBehindJournal_StillCounts()
    {
        var text = ThreeQuestions(
            "- Q1: Found: old. Question: back? [go on -> active | finish -> done | back -> queue] -> **back** (developer, 2026-11-21 19:40)",
            "- Q2: Found: the fix is pushed. Question: done? [go on -> active | finish -> done | back -> queue]",
            "- Q3: Found: nothing. Question: finish? [go on -> active | finish -> done] -> **finish** (developer, 2026-11-21 21:50)",
            journalLast: "2026-11-21 22:06 Resumed, fixed it, and pushed.");

        var edit = CardMoves.Answer(Card("active", text), text, 2, 1, "", On(21, 55));

        Assert.True(edit.Ok, edit.Error);
        Assert.Equal(CardFolder.Done, edit.Folder);
        Assert.Equal("done", edit.Status);
    }

    // A card waiting on the developer with three questions: Q1 and Q2 answered, Q3 still open.
    private static string ThreeQuestions(string q1, string q2, string q3, string? journalLast = null) => string.Join('\n',
        "Status: needs-human: waiting",
        "",
        "## Questions",
        "",
        q1,
        q2,
        q3,
        "",
        "## Result",
        "",
        journalLast ??
            "2026-11-21 19:01 Worked and put the card up for review with Q1 to Q3." +
            "\n- 2026-11-21 20:30 developer: sent back from review from the page; read any ## Dev section first." +
            "\n2026-11-21 22:06 Resumed, fixed it, and pushed.");

    private static DateTimeOffset On(int hour, int minute) => new(2026, 11, 21, hour, minute, 0, TimeSpan.Zero);

    // A card waiting on the developer: two questions under ## Questions and the Result whose last
    // line is the one the worker wrote when it parked the card.
    private static string Waited(string q1, string q2, string journalLast) => string.Join('\n',
        "Status: needs-human: waiting",
        "",
        "## Questions",
        "",
        q1,
        q2,
        "",
        "## Result",
        "",
        journalLast);

    private static string Line(string stamp, string text) => $"\n{stamp} {text}";

    private static string JournalTime(int hour, int minute) => $"- {At(hour, minute).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)}:";

    private static DateTimeOffset At(int hour, int minute) => new(2026, 11, 19, hour, minute, 0, TimeSpan.Zero);

    // MOVE-2: an answered question is an error and nothing is to be committed - whether the page's
    // copy of the card still shows it open or not; so is a choice out of range.
    [Fact]
    public void Moves_Answer_AlreadyAnswered_IsError()
    {
        var open = "Status: x\n## Questions\n- Q1: Ok? [yes | no]\n";
        var answered = "Status: x\n## Questions\n- Q1: Ok? [yes | no] -> **yes** (developer, 2026-11-18 11:00)\n";

        var stale = CardMoves.Answer(Card("active", open), answered, 1, 0, "", Now);
        var known = CardMoves.Answer(Card("active", answered), answered, 1, 0, "", Now);
        var range = CardMoves.Answer(Card("active", open), open, 1, 2, "", Now);

        Assert.False(stale.Ok);
        Assert.Equal("Q1 on 010-A is already answered", stale.Error);
        Assert.Equal("", stale.Text);
        Assert.Equal("", stale.Message);
        Assert.False(known.Ok);
        Assert.Equal("no open question Q1 on 010-A", known.Error);
        Assert.False(range.Ok);
    }
}
