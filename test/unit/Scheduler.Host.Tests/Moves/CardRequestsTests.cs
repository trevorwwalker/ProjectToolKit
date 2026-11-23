using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Core.Moves;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// Card 660-B (roadmap item 2): Core's card move requests - parsing, order, and what a request
/// makes of main's text, conflicting requests included. Here rather than in Core.Tests because the
/// card's scope has Host's tests and Core's Moves folder. Design: Scheduler.Core/Moves/card-moves.md.
/// </summary>
public class CardRequestsTests
{
    private const string Jobs = "jobs";

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 2, 0, 0, TimeSpan.Zero);

    private static string Body(string status, string result = "", string dev = "", string questions = "") =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n## Questions\n\n" + questions + dev + "## Result\n\n" + result;

    private const string Q1 = "- Q1: Which way? [yes -> done | no, leave it]";
    private const string Q1Answered = Q1 + " -> **yes** (developer, 2026-11-19 09:15)";
    private const string Q2 = "- Q2: Keep the old name? [yes | no]";

    private static int Count(string text, string prefix) =>
        text.Replace("\r\n", "\n").Split('\n').Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

    private static Card CardIn(string folder, string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folder}/010-A-x.md", text) ?? throw new InvalidOperationException("not a card path");

    private static CardRequest Req(string header, string? text = null) =>
        CardRequests.Parse("20261119-020000-000-slot1.move", header + (text is null ? "" : "---\n" + text));

    // 660-B: header lines up to ---, then the card's text; From defaults to active; a BOM is dropped.
    [Fact]
    public void Parse_HeaderAndText()
    {
        var r = CardRequests.Parse("a.move", "\uFEFFCard: 010-A-x.md\r\nTo: Review\r\nSlot: 4\r\n---\r\n# 010-A: x\r\nStatus: review: check it\r\n");
        Assert.True(r.Ok, r.Error);
        Assert.Equal("010-A-x", r.Card);
        Assert.Equal(CardFolder.Active, r.From);
        Assert.Equal(CardFolder.Review, r.To);
        Assert.Equal(4, r.Slot);
        Assert.False(r.Claim);
        Assert.Equal("# 010-A: x\r\nStatus: review: check it\r\n", r.Text);

        var claim = CardRequests.Parse("b.move", CardRequests.ClaimText("010-A-x", 2));
        Assert.True(claim.Ok, claim.Error);
        Assert.True(claim.Claim);
        Assert.Equal(CardFolder.Queue, claim.From);
        Assert.Equal(CardFolder.Active, claim.To);
        Assert.Null(claim.Text);
    }

    // 660-B: a request that cannot be read is an error, never a guess.
    [Theory]
    [InlineData("To: done\n", "no Card")]
    [InlineData("Card: 010-A-x\n", "To:")]
    [InlineData("Card: 010-A-x\nTo: elsewhere\n", "To:")]
    [InlineData("Card: 010-A-x\nTo: done\nFrom: nowhere\n", "From:")]
    [InlineData("Card: 010-A-x\nmove it please\nTo: done\n", "not a header line")]
    public void Parse_Bad_IsError(string content, string error)
    {
        var r = CardRequests.Parse("x.move", content);
        Assert.False(r.Ok);
        Assert.Contains(error, r.Error);
        Assert.False(CardRequests.Plan(CardIn("active", Body("ready")), Body("ready"), r, Now).Ok);
    }

    // 660-B: requests are applied in file-name order, time first, then slot; only .move files.
    [Fact]
    public void Order_ByNameTimeFirst()
    {
        var a = CardRequests.FileName(new DateTimeOffset(2026, 11, 19, 2, 0, 0, 5, TimeSpan.Zero), 3);
        var b = CardRequests.FileName(new DateTimeOffset(2026, 11, 19, 2, 0, 0, 7, TimeSpan.Zero), 1);
        var c = CardRequests.FileName(new DateTimeOffset(2026, 11, 19, 2, 0, 0, 7, TimeSpan.Zero), 2);
        Assert.Equal("20261119-020000-005-slot3.move", a);
        Assert.Equal([a, b, c], CardRequests.Order([c, "x.move.tmp", b, "notes.txt", a]));
    }

    // 660-B: a claim moves a queued card to active/, adds Claimed under Status, and keeps the text.
    [Fact]
    public void Plan_Claim_MovesAndAddsClaimed()
    {
        var text = Body("ready");
        var e = CardRequests.Plan(CardIn("queue", text), text, CardRequests.Parse("c.move", CardRequests.ClaimText("010-A-x", 2)), Now);
        Assert.True(e.Ok, e.Error);
        Assert.Equal("jobs/queue/010-A-x.md", e.Path);
        Assert.Equal("jobs/active/010-A-x.md", e.NewPath);
        Assert.Equal("jobs: start 010-A-x", e.Message);
        Assert.Contains("Status: ready\nClaimed: 2026-11-19, lane A\n", e.Text);
    }

    // 660-B: conflicting requests - a second claim of a claimed card fails, so one copy is left;
    // a move from a folder the card has already left (to anywhere but To) fails.
    [Fact]
    public void Plan_Conflicts()
    {
        var text = Body("ready");
        var claim = CardRequests.Parse("c.move", CardRequests.ClaimText("010-A-x", 2));
        var again = CardRequests.Plan(CardIn("active", text), text, claim, Now);
        Assert.False(again.Ok);
        Assert.Contains("already claimed", again.Error);

        var finish = Req("Card: 010-A-x\nTo: review\n", Body("review: look"));
        var gone = CardRequests.Plan(CardIn("done", Body("done")), Body("done"), finish, Now);
        Assert.False(gone.Ok);
        Assert.Contains("is in done/, not active/", gone.Error);

        var other = CardRequests.Plan(CardIn("active", text), text, Req("Card: 020-B-y\nTo: done\n"), Now);
        Assert.False(other.Ok);
    }

    // 660-B: a card already in To (a worker that moved it itself) gets the request's text in place.
    [Fact]
    public void Plan_AlreadyThere_UpdatesInPlace()
    {
        var main = Body("review");
        var e = CardRequests.Plan(CardIn("review", main), main, Req("Card: 010-A-x\nTo: review\n", Body("review: check the page", "- done it\n")), Now);
        Assert.True(e.Ok, e.Error);
        Assert.False(e.Moves);
        Assert.Equal("jobs: status 010-A-x", e.Message);
        Assert.Contains("- done it", e.Text);
        Assert.Contains("Status: review: check the page", e.Text);
    }

    // 660-B: the worker's text is written; done/ gets Status done, review/ a Status starting review,
    // queue/ and active/ the text's own Status; without text, main's text and the Status header.
    [Fact]
    public void Plan_Finish_StatusAndText()
    {
        var main = Body("ready");
        var card = CardIn("active", main);

        var done = CardRequests.Plan(card, main, Req("Card: 010-A-x\nTo: done\n", Body("ready", "- all green\n")), Now);
        Assert.Equal("jobs/done/010-A-x.md", done.NewPath);
        Assert.Equal("jobs: done 010-A-x", done.Message);
        Assert.Contains("Status: done\n", done.Text);
        Assert.Contains("- all green", done.Text);

        var review = CardRequests.Plan(card, main, Req("Card: 010-A-x\nTo: review\n", Body("ready", "- r\n")), Now);
        Assert.Contains("Status: review\n", review.Text);
        var asked = CardRequests.Plan(card, main, Req("Card: 010-A-x\nTo: review\n", Body("review: try it on the PC", "- r\n")), Now);
        Assert.Contains("Status: review: try it on the PC\n", asked.Text);

        var wait = CardRequests.Plan(card, main, Req("Card: 010-A-x\nTo: queue\n", Body("waiting: planner (request #3)")), Now);
        Assert.Equal("jobs/queue/010-A-x.md", wait.NewPath);
        Assert.Equal("jobs: wait 010-A-x", wait.Message);
        Assert.Contains("Status: waiting: planner (request #3)\n", wait.Text);

        var park = CardRequests.Plan(card, main, Req("Card: 010-A-x\nTo: active\nStatus: needs-human: which way?\n"), Now);
        Assert.False(park.Moves);
        Assert.Equal(main.Replace("Status: ready", "Status: needs-human: which way?"), park.Text);
    }

    // 660-B: a ## Dev reply on main that the worker's text lacks is kept, before ## Result.
    [Fact]
    public void Plan_KeepsDevReply()
    {
        var main = Body("ready", "", "## Dev\n\nUse the slow path.\n\n");
        var worker = Body("review: look", "- did it\n");
        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", worker), Now);
        Assert.True(e.Ok, e.Error);
        Assert.Contains("## Dev\n\nUse the slow path.\n\n## Result\n\n- did it", e.Text);

        // Already in the worker's text: not doubled.
        Assert.Equal(main, CardRequests.KeepDev(main, main));
    }

    // 780-B (CM-4): a worker's finish keeps the answers made on main while it ran - main's answered
    // Q1 survives a worker whose text still has the same question unanswered, once only.
    [Fact]
    public void Plan_KeepsAnsweredQuestion()
    {
        var main = Body("ready", "- older\n", questions: Q1Answered + "\n\n");
        var worker = Body("review: look", "- did it\n", questions: Q1 + "\n\n");

        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", worker), Now);

        Assert.True(e.Ok, e.Error);
        Assert.Contains(Q1Answered + "\n", e.Text);
        Assert.Equal(1, Count(e.Text, "- Q1:"));
        Assert.DoesNotContain("Q1", e.Log);
    }

    // 780-B: a question only main has (the planner's, say) is kept, in the worker's Questions section;
    // the worker's own questions are its own.
    [Fact]
    public void Plan_KeepsQuestionAddedOnMain()
    {
        var main = Body("ready", "- older\n", questions: Q1 + "\n\n" + Q2 + "\n\n");
        var worker = Body("review: look", "- did it\n", questions: Q1 + "\n\n");

        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", worker), Now);

        Assert.True(e.Ok, e.Error);
        Assert.Contains(Q2 + "\n", e.Text);
        Assert.Contains(Q1 + "\n", e.Text);
        Assert.Equal(1, Count(e.Text, "- Q1:"));
        Assert.True(
            e.Text.IndexOf(Q2, StringComparison.Ordinal) < e.Text.IndexOf("## Result", StringComparison.Ordinal),
            e.Text);
    }

    // 780-B: everything else is the worker's - its Status, its Result, and its own wording of its own
    // questions; main keeps the questions it has that the worker's text lacks.
    [Fact]
    public void Plan_WorkerTextWins()
    {
        var main = Body("ready", "- older\n", questions: Q1 + "\n\n" + Q2 + "\n\n");
        var worker = Body("review: look", "- all green\n", questions: Q2 + "\n\n- Q9: The worker's own? [a | b]\n\n");

        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", worker), Now);

        Assert.True(e.Ok, e.Error);
        Assert.Contains("Status: review: look\n", e.Text);
        Assert.Contains("- all green", e.Text);
        Assert.DoesNotContain("- older", e.Text);
        Assert.Contains(Q1 + "\n", e.Text);
        Assert.Equal(1, Count(e.Text, "- Q1:"));
        Assert.Contains("- Q9: The worker's own? [a | b]\n", e.Text);
        Assert.Equal(1, Count(e.Text, "- Q2:"));
        Assert.Equal("jobs/review/010-A-x.md", e.NewPath);
    }

    // 780-B: a question answered on main that the worker reworded or removed keeps main's answered
    // line, and the log says so.
    [Fact]
    public void Plan_RewordedAnsweredQuestion_KeepsMainLineAndLogs()
    {
        var main = Body("ready", "- older\n", questions: Q1Answered + "\n\n");
        var reworded = Body("review: look", "- r\n", questions: "- Q1: Which way? [a | b]\n\n");

        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", reworded), Now);

        Assert.True(e.Ok, e.Error);
        Assert.Contains(Q1Answered + "\n", e.Text);
        Assert.DoesNotContain("[a | b]", e.Text);
        Assert.Equal(1, Count(e.Text, "- Q1:"));
        Assert.Contains("Q1", e.Log);
        Assert.Contains("reworded", e.Log);
    }

    // 780-B: the same, when the worker's text has no such question at all - and a worker text with no
    // Questions section gets one before ## Result.
    [Fact]
    public void Plan_DroppedAnsweredQuestion_KeepsMainLineAndLogs()
    {
        var main = Body("ready", "- older\n", questions: Q1Answered + "\n\n");

        var dropped = CardRequests.Plan(
            CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", Body("review: look", "- r\n", questions: "\n")), Now);
        var noSection = CardRequests.Plan(
            CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", Body("review: look", "- r\n").Replace("## Questions\n\n", "")), Now);

        Assert.True(dropped.Ok, dropped.Error);
        Assert.Contains(Q1Answered + "\n", dropped.Text);
        Assert.Contains("dropped", dropped.Log);
        Assert.True(noSection.Ok, noSection.Error);
        Assert.Contains(Q1Answered + "\n", noSection.Text);
        Assert.True(
            noSection.Text.IndexOf("- Q1:", StringComparison.Ordinal) < noSection.Text.IndexOf("## Result", StringComparison.Ordinal),
            noSection.Text);
    }

    // 780-B: the developer's note under an answered question is kept with it, and the worker's own line
    // endings are kept.
    [Fact]
    public void Plan_KeepsAnsweredQuestionWithItsNote_AndLineEndings()
    {
        var main = Body("ready", "- older\n", questions: Q1Answered + "\n  Note: pick the fast one.\n\n");
        var worker = Body("review: look", "- r\n", questions: Q1 + "\n\n");

        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", worker), Now);
        var lf = CardRequests.Plan(
            CardIn("active", main), main, Req("Card: 010-A-x\nTo: review\n", worker.Replace("\n", "\r\n")), Now);

        Assert.True(e.Ok, e.Error);
        Assert.Contains(Q1Answered + "\n  Note: pick the fast one.\n", e.Text);
        Assert.Contains("\r\n" + Q1Answered + "\r\n", lf.Text);
    }

    // 780-B: a card with no questions on main, or no text from the worker, comes back untouched.
    [Fact]
    public void Plan_NothingToKeep_LeavesTextAsItIs()
    {
        var worker = Body("review: look", "- r\n", questions: Q1 + "\n\n");

        Assert.Equal(worker, CardRequests.KeepAnswers(Body("ready"), worker));
        Assert.Equal(worker, CardRequests.KeepAnswers(Body("ready", questions: "\n"), worker));

        var main = Body("ready", "- older\n", questions: Q1Answered + "\n\n");
        var e = CardRequests.Plan(CardIn("active", main), main, Req("Card: 010-A-x\nTo: active\n"), Now);
        Assert.True(e.Ok, e.Error);
        Assert.Equal(main, e.Text);
        Assert.DoesNotContain("Q1", e.Log);
    }

    // 660-B: the prompt tells the worker how to hand a card over, on one line with no double quotes.
    [Fact]
    public void Prompt_SaysMoveRequests()
    {
        var config = SchedulerConfigLoader.Load("{}", @"C:\repos\Proj", @"C:\Users\dev\AppData\Local") with { LogDir = @"C:\logs" };
        var text = Body("ready");
        var prompt = WorkerPrompt.Build(config, new WorkerPromptRequest { Card = CardIn("queue", text), Worktree = @"C:\w\slot3", Slot = 3, Start = Now, Jobs = 4 });
        Assert.Contains(@"C:\logs\moves\' + (Get-Date -Format yyyyMMdd-HHmmss-fff) + '-slot3'", prompt);
        Assert.Contains("'Card: 010-A-x'", prompt);
        Assert.Contains("Never git mv a card", prompt);
        Assert.Contains("The scheduler assigned it to you and claimed it", prompt);
        Assert.Contains("with a move request", prompt);
        Assert.Contains("no claim step to run", prompt);
        Assert.DoesNotContain("\"", prompt);
        Assert.DoesNotContain("\n", prompt);
    }
}
