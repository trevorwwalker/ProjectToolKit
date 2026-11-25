// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// The unanswered questions under <c>## Questions</c> (CARD-12, Parse-Question L503): only lines in
/// that section count, a question needs at least two choices, and a choice ending
/// <c>-&gt; done|queue|active</c> carries where choosing it moves the card. An answered line has
/// the answer after the bracket, so it no longer matches. Test plan: test-plans/cards.md.
/// </summary>
public class CardQuestionsTests
{
    private const string Jobs = "docs/jobs";

    private static Card ParseQueue(string name, string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/queue/{name}", text)
        ?? throw new InvalidOperationException($"not a card path: {name}");

    // CARD-12.
    [Fact]
    public void Cards_Questions_ParsedWithChoicesAndDestinations()
    {
        var card = ParseQueue(
            "010-A-x.md",
            """
            # T
            Status: needs-human: pick

            ## Questions
            - Q1: Ok? [yes -> done | no -> queue | later]
            - Q2: One? [only]
            - Q3: Done? [a | b] -> **a** (developer, 2026-11-18 10:00)

            ## Result
            - Q4: Ignored? [a | b]
            """);

        var q = Assert.Single(card.Questions);
        Assert.Equal(1, q.Number);
        Assert.Equal("Ok?", q.Text);
        Assert.Equal(
            [
                new CardChoice { Label = "yes", Destination = CardFolder.Done },
                new CardChoice { Label = "no", Destination = CardFolder.Queue },
                new CardChoice { Label = "later", Destination = null },
            ],
            q.Choices);

        // Nothing before the first `## ` header is a question, and the section ends at the next one.
        Assert.Empty(ParseQueue("010-A-x.md", "- Q1: Before? [a | b]").Questions);
        var oneSection = Assert.Single(
            ParseQueue(
                "010-A-x.md",
                "## Questions\n- Q1: First? [a | b]\n## Result\n- Q2: After? [a | b]").Questions);
        Assert.Equal("First?", oneSection.Text);

        // `-> active` moves the card too; a destination outside the three is part of the label, and
        // the spaces around a choice are trimmed (L509-513).
        var moved = Assert.Single(
            ParseQueue(
                "010-A-x.md",
                "## Questions\n- Q1: Where? [ back to the queue -> active | park -> review ]")
            .Questions);
        Assert.Equal("back to the queue", moved.Choices[0].Label);
        Assert.Equal(CardFolder.Active, moved.Choices[0].Destination);
        Assert.Equal("park -> review", moved.Choices[1].Label);
        Assert.Null(moved.Choices[1].Destination);

        // An empty choice between separators is no choice, so this is still a two-choice question.
        Assert.Single(
            ParseQueue("010-A-x.md", "## Questions\n- Q1: Blank? [a |  | b]").Questions);

        // A question line with no bracket is not a question at all.
        Assert.Empty(ParseQueue("010-A-x.md", "## Questions\n- Q1: No bracket here").Questions);

        // Parse is the same rule for one line: an answered line gives nothing, and the separators
        // need no spaces around them (L504-513).
        Assert.Null(CardQuestions.Parse("- Q1: Ok? [a | b] -> **a** (developer)"));
        Assert.Equal(2, CardQuestions.Parse("- Q2: Ok? [a|b]")!.Choices.Count);
    }

    // CARD-12 (820-A): a question the worker wrapped over several lines is one question - the lines
    // under it belong to it, so its text and its choices all survive the parse.
    [Fact]
    public void Cards_Questions_WrappedQuestionKeepsItsTextAndChoices()
    {
        var card = ParseQueue(
            "010-A-x.md",
            """
            # T

            ## Questions
            - Q1: Found: the three answers the developer gave on 760-A were lost because each
              question was written over three lines and the reader only ever saw the first
              one, so a click on the page did nothing at all. Question: read the whole
              question, the way a worker writes it?
              [yes, read the wrapped lines -> done | no, only the first line -> queue]
            - Q2: Single? [a | b]

            ## Result
            """);

        Assert.Equal(2, card.Questions.Count);
        var q = card.Questions[0];
        Assert.Equal(1, q.Number);
        Assert.Equal(
            "Found: the three answers the developer gave on 760-A were lost because each question was written over three lines and the reader only ever saw the first one, so a click on the page did nothing at all. Question: read the whole question, the way a worker writes it?",
            q.Text);
        Assert.Equal(
            [
                new CardChoice { Label = "yes, read the wrapped lines", Destination = CardFolder.Done },
                new CardChoice { Label = "no, only the first line", Destination = CardFolder.Queue },
            ],
            q.Choices);

        // A question on one line parses exactly as it always did, next to a wrapped one.
        Assert.Equal("Single?", card.Questions[1].Text);
    }

    // CARD-12 (820-A): the continuation lines are the ones that are not blank, not another `- Q`
    // line, not a `## ` heading and not the `  Note:` line the page puts under an answer.
    [Fact]
    public void Cards_Questions_WrappedQuestionEndsAtBlankHeadingAndNote()
    {
        const string Questions = """
            ## Questions
            - Q1: Ok? [
              go on -> active | leave it]

            a line after a blank line is no part of the question
            - Q2: More? [a | b] and what follows the bracket, which is no question at all

            """;

        // Q1's choices sit on the line below it; the text after the blank line is not joined to it.
        var wrapped = ParseQueue("010-A-x.md", Questions + "\n## Result\n").Questions;
        var q1 = Assert.Single(wrapped);
        Assert.Equal(1, q1.Number);
        Assert.Equal("Ok?", q1.Text);
        Assert.Equal(
            [
                new CardChoice { Label = "go on", Destination = CardFolder.Active },
                new CardChoice { Label = "leave it", Destination = null },
            ],
            q1.Choices);

        // A `## ` heading ends a question too: what follows it is no part of it.
        var afterHeading = ParseQueue(
            "010-A-x.md",
            "## Questions\n- Q1: Ok?\n  [a | b]\n## Result\n  [c | d]\n").Questions;
        var heading = Assert.Single(afterHeading);
        Assert.Equal("Ok?", heading.Text);

        // The note the page writes under an answer is no continuation of it, and the next `- Q`
        // line always starts a question of its own.
        var noted = ParseQueue(
            "010-A-x.md",
            "## Questions\n- Q1: Ok?\n  [a | b]\n  Note: not part of the question\n- Q2: More?\n  [c | d]\n").Questions;
        Assert.Equal(2, noted.Count);
        Assert.Equal("More?", noted[1].Text);
    }

    // CARD-12 (820-A): the shape 760-A's card really had - three questions, each wrapped with the
    // continuation lines at column 0 and the choices ending the last one - is read whole.
    [Fact]
    public void Cards_Questions_WrappedQuestion_AsWorkersActuallyWriteThem()
    {
        var card = ParseQueue(
            "760-A-x.md",
            """
            # 760-A: X

            ## Questions
            - Q1: Found: the 530-D card has a sentence that wraps onto a new line beginning "worker:" followed by
            file paths, and the old parser read that as the card's worker request. Question: reword that sentence so
            it no longer reads like a field? [yes, ask the planner to have the design lane reword it | no, leave the
            card as it is -> done] -> **no, leave the card as it is** (developer, 2026-11-19)
            - Q2: Found: a test required a card's fields to be read from every line of the card, so prose could set
            them; they are now read from the header only. Question: accept that a card's sections can no longer set
            its fields? [yes, and have the planner write the header rule into the spec | no, put it back -> queue]
            - Q3: Found: a slot that is only busy still reads "next up; waiting for worker Opus". Question: show the
            busy reason too? [yes, card it for the host lane with a design lane spec line | no, leave it -> done]

            ## Result
            """);

        // Q1 already carries the developer's answer, so only the two that were never answered are open.
        var open = card.Questions;
        Assert.Equal(2, open.Count);
        Assert.Equal(2, open[0].Number);
        Assert.Equal(
            "Found: a test required a card's fields to be read from every line of the card, so prose could set them; they are now read from the header only. Question: accept that a card's sections can no longer set its fields?",
            open[0].Text);
        Assert.Equal(
            [
                new CardChoice { Label = "yes, and have the planner write the header rule into the spec", Destination = null },
                new CardChoice { Label = "no, put it back", Destination = CardFolder.Queue },
            ],
            open[0].Choices);
        Assert.Equal(3, open[1].Number);
        Assert.Equal("yes, card it for the host lane with a design lane spec line", open[1].Choices[0].Label);
        Assert.Equal(CardFolder.Done, open[1].Choices[1].Destination);
    }

    // CARD-12 (820-A): ParseBlock is the same rule over lines: it reads the block from the line it
    // is given and says where the block ended, and answers on the line the closing bracket is on.
    [Fact]
    public void Cards_Questions_ParseBlockReadsTheBlockAndSaysWhereItEnded()
    {
        string[] lines =
        [
            "## Questions",
            "- Q1: Found: it is",
            "  broken. Question: fix?",
            "  [yes -> done | no -> queue]",
            "",
            "- Q2: Next? [a -> done | b] -> **a** (developer, 2026-11-18 10:00)",
            "  Note: a note",
        ];

        var open = CardQuestions.ParseBlock(lines, 1, out var end);
        Assert.NotNull(open);
        Assert.Equal(3, end);
        Assert.Equal("Found: it is broken. Question: fix?", open.Text);
        Assert.Equal(2, open.Choices.Count);

        // A line that is not the start of a question is no question, and holds no block of its own.
        Assert.Null(CardQuestions.ParseBlock(lines, 2, out var mid));
        Assert.Equal(3, mid);
        Assert.Null(CardQuestions.ParseBlock(lines, 0, out var heading));
        Assert.Equal(0, heading);

        // The answered form: the question as it was asked, and the label the developer answered.
        var asked = CardQuestions.ParseAnswered(lines, 5, out var answeredEnd, out var answer);
        Assert.NotNull(asked);
        Assert.Equal(5, answeredEnd);
        Assert.Equal("a", answer);
        Assert.Equal(CardFolder.Done, asked.Choices[0].Destination);

        // An open question is not an answered one, and the reverse holds too.
        Assert.Null(CardQuestions.ParseAnswered(lines, 1, out _, out _));
        Assert.Null(CardQuestions.ParseBlock(lines, 5, out _));

        // Past the end there is nothing to read.
        Assert.Null(CardQuestions.ParseBlock([], 0, out var none));
        Assert.Equal(0, none);
    }
}
