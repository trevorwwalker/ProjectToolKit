// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// Card paths and text fields (A2: CARD-4..CARD-11). Pure parsing: paths in, <see cref="Card"/>
/// out; no git (that is Host, card 040-B). Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class CardFilesTests
{
    private const string Jobs = "docs/jobs";

    private static Card Parse(string path, string text) =>
        CardFiles.Parse(Jobs, path, text) ?? throw new InvalidOperationException($"not a card path: {path}");

    private static Card ParseQueue(string name, string text) => Parse($"{Jobs}/queue/{name}", text);

    // CARD-4.
    [Fact]
    public void Cards_PathPattern_FieldsAndId()
    {
        var first = CardFiles.FromPath(Jobs, "docs/jobs/queue/7-B-slug.md");
        Assert.NotNull(first);
        Assert.Equal(CardFolder.Queue, first.Folder);
        Assert.Equal("docs/jobs/queue/7-B-slug.md", first.Path);
        Assert.Equal("7-B-slug", first.Name);
        Assert.Equal(7L, first.Num);
        Assert.Equal("B", first.Category);
        // 931-A (developer, 2026-11-19): the id is the number exactly as the file name writes it.
        Assert.Equal("7-B", first.Id);

        var claimed = CardFiles.FromPath(Jobs, "docs/jobs/active/010-A-x.md");
        Assert.NotNull(claimed);
        Assert.Equal(CardFolder.Active, claimed.Folder);
        Assert.Equal("010-A", claimed.Id);

        var finished = CardFiles.FromPath(Jobs, "docs/jobs/done/1200-C-y.md");
        Assert.NotNull(finished);
        Assert.Equal(CardFolder.Done, finished.Folder);
        Assert.Equal("1200-C", finished.Id);
    }

    // CARD-4. Boundary: malformed file names.
    [Fact]
    public void Cards_NonMatchingFiles_Ignored()
    {
        string[] paths =
        [
            "docs/jobs/queue/README.md",
            "docs/jobs/queue/010-AB-x.md",
            "docs/jobs/queue/x-A-010.md",
            "docs/jobs/queue/sub/010-A-x.md",
            "docs/jobs/plans/010-A-x.md",
            "docs/jobs/queue/010-A-x.txt",
        ];
        foreach (var path in paths)
        {
            Assert.Null(CardFiles.FromPath(Jobs, path));
            Assert.Null(CardFiles.Read(Jobs, path, _ => throw new InvalidOperationException("blob read for a non-card")));
            Assert.Null(CardFiles.Parse(Jobs, path, "Status: ready"));
        }
    }

    // CARD-5.
    [Fact]
    public void Cards_DoneFolder_NotOpened()
    {
        var reads = 0;
        var card = CardFiles.Read(Jobs, "docs/jobs/done/010-A-x.md", _ =>
        {
            reads++;
            return "\u0000 not text at all \u0000";
        });
        Assert.NotNull(card);
        Assert.Equal(CardFolder.Done, card.Folder);
        Assert.Equal("010-A-x", card.Name);
        Assert.Equal("010-A", card.Id);
        Assert.Equal("A", card.Category);
        Assert.Equal("", card.Status);
        Assert.Equal(0, reads);
    }

    // CARD-6 (L617: only ^#\s+ is a title, so ## lines are not).
    [Fact]
    public void Cards_Title_FirstHashSpaceLine()
    {
        var card = ParseQueue("010-A-x.md", "## Not\n# First\n# Second");
        Assert.Equal("First", card.Title);
    }

    // CARD-7.
    [Fact]
    public void Cards_Status_FirstLineAndContinuations()
    {
        var card = ParseQueue(
            "010-A-x.md",
            "Status: needs-human: long\n   question here\n Status: no\nStatus: second");
        Assert.Equal("needs-human: long question here", card.Status);
    }

    // CARD-7, DISP-10 (L2903): no Status line means Status '', which does not start "ready",
    // so the card is never a candidate and its queue note is the empty Status itself.
    [Fact]
    public void Cards_MalformedCard_NoStatusLine_IsNotReady()
    {
        var titleOnly = ParseQueue("010-A-x.md", "# Only a title\n");
        var empty = ParseQueue("020-B-y.md", "");
        foreach (var card in new[] { titleOnly, empty })
        {
            Assert.Equal("", card.Status);
            Assert.Equal(1, card.Prio);
            Assert.Empty(card.Depends);
            Assert.Null(card.TimeBox);
            Assert.Null(card.WorkerReq);
            Assert.False(Text.IsMatch(card.Status, "^ready")); // DISP-10: not a candidate
            Assert.Equal("", card.Status);                     // DISP-10: the note is the Status
        }

        // No exception, and the other cards of the batch are still read and ready.
        var good = ParseQueue("030-C-z.md", "# T\nStatus: ready\n");
        Assert.Equal("ready", good.Status);
        Assert.True(Text.IsMatch(good.Status, "^ready"));
    }

    // CARD-8.
    [Fact]
    public void Cards_Priority_Mapping_LastWins()
    {
        Assert.Equal(1, ParseQueue("010-A-x.md", "# t").Prio);
        Assert.Equal(0, ParseQueue("010-A-x.md", "Priority: high").Prio);
        Assert.Equal(2, ParseQueue("010-A-x.md", "Priority: low").Prio);
        Assert.Equal(1, ParseQueue("010-A-x.md", "Priority: urgent").Prio);
        Assert.Equal(2, ParseQueue("010-A-x.md", "Priority: high\nPriority: low").Prio);
        Assert.Equal(1, ParseQueue("010-A-x.md", "Priority:").Prio);
    }

    // CARD-6..CARD-11 (760-A): the field lines are the header's. A card's own sections cannot set
    // its fields - a Task or Result line that begins "worker: ...", "Priority: ..." or "Depends on:
    // ..." used to replace the header's, and the card then asked for a worker no slot is.
    [Fact]
    public void Cards_FieldLinesInsideSections_AreIgnored()
    {
        var card = ParseQueue(
            "010-A-x.md",
            "# T\nPriority: high\n\n## Result\nPriority: low\nTime box: 45\nDepends on: 010-A\n");
        Assert.Equal(0, card.Prio);
        Assert.Null(card.TimeBox);
        Assert.Empty(card.Depends);
    }

    // CARD-6..CARD-11 (760-A): the header keeps working as before - fields before the first section.
    [Fact]
    public void Cards_FieldLinesInHeader_StillCount()
    {
        var card = ParseQueue(
            "010-A-x.md",
            "# T\nPriority: high\nTime box: 45\nDepends on: 010-A\n\n## Result\nnothing yet\n");
        Assert.Equal(0, card.Prio);
        Assert.Equal(45, card.TimeBox);
        Assert.Equal(["010-A"], card.Depends);
    }

    // CARD-10 (760-A, 530-D): a Task line that happens to start with the lowercase word "worker:" is
    // prose, not the Worker: line; the header's Worker: line must win.
    [Fact]
    public void Cards_WorkerRequest_ProseWorkerLineInTask_Ignored()
    {
        var text =
            "# 530-D: Plan worker types with caps\n\nLane: D\nStatus: ready\nWorker: Opus\n\n## Task\n\n"
            + "Read the code that today chooses a\nworker: `code/src/Scheduler.Core/Dispatch/`, `Lanes/WorkerSelection.cs`,\n"
            + "`Slots/`, and the page's worker windows.\n";
        var card = ParseQueue("530-D-plan-worker-types.md", text);

        Assert.Equal("Opus", card.WorkerReq);
    }

    // CARD-9.
    [Fact]
    public void Cards_TimeBox_ParsedLastWins()
    {
        Assert.Equal(90, ParseQueue("010-A-x.md", "Time box: 90 min").TimeBox);
        Assert.Equal(60, ParseQueue("010-A-x.md", "Time box: 30\nTime box: 60").TimeBox);
        Assert.Null(ParseQueue("010-A-x.md", "Time box: none").TimeBox);
        Assert.Null(ParseQueue("010-A-x.md", "# t").TimeBox);
    }

    // CARD-10.
    [Fact]
    public void Cards_WorkerRequest_TextBeforeParenthesis()
    {
        Assert.Equal("Opus", ParseQueue("010-A-x.md", "Worker: Opus (premium, for the core)").WorkerReq);
        Assert.Equal("3", ParseQueue("010-A-x.md", "Worker: 3").WorkerReq);
        Assert.Equal("qwen 32b", ParseQueue("010-A-x.md", "Worker:  qwen 32b  ").WorkerReq);
        Assert.Null(ParseQueue("010-A-x.md", "# t").WorkerReq);
    }

    // CARD-11. Any width counts now (931-A), so the "(and 30-C)" aside is an id like any other.
    [Fact]
    public void Cards_DependsOn_AllIdsLastLineWins()
    {
        Assert.Equal(
            ["010-A", "020-B", "30-C"],
            ParseQueue("010-A-x.md", "Depends on: 010-A, 020-B (and 30-C)").Depends);
        Assert.Empty(ParseQueue("010-A-x.md", "Depends on: 010-A\nDepends on: none").Depends);
    }

    // CARD-11 (931-A, developer 2026-11-19): a card number is a positive integer of any width, so
    // \b\d+-[A-Z]\b matches it; a lower-case category letter still matches nothing (G-1).
    [Fact]
    public void Cards_DependsOn_IdsOfAnyWidth()
    {
        Assert.Equal(
            ["1000-A", "930-B"],
            ParseQueue("1001-A-x.md", "Depends on: 1000-A, 930-B").Depends);
        Assert.Equal(
            ["7-A", "1200-B"],
            ParseQueue("12-A-x.md", "Depends on: 7-A, then 1200-B when it lands").Depends);
        Assert.Empty(ParseQueue("010-A-x.md", "Depends on: 1200-a").Depends);
    }

    // CARD-4 (931-A): the id keeps the width the file name has - no padding, no stripping - while
    // Num keeps the numeric value.
    [Fact]
    public void Cards_Id_KeepsTheWidthTheFileNameWrites()
    {
        var wide = CardFiles.FromPath(Jobs, "docs/jobs/queue/1000-A-x.md");
        Assert.NotNull(wide);
        Assert.Equal("1000-A", wide.Id);
        Assert.Equal("1000-A-x", wide.Name);
        Assert.Equal(1000L, wide.Num);

        var padded = CardFiles.FromPath(Jobs, "docs/jobs/queue/007-A-x.md");
        Assert.NotNull(padded);
        Assert.Equal("007-A", padded.Id);
        Assert.Equal(7L, padded.Num);

        var narrow = CardFiles.FromPath(Jobs, "docs/jobs/queue/40-A-x.md");
        Assert.NotNull(narrow);
        Assert.Equal("40-A", narrow.Id);
        Assert.Equal(40L, narrow.Num);
    }

    // CARD-4 (931-A): order is by the number, so 999-A comes before 1000-A and the page's tables
    // and the dispatch order never put a four-digit card ahead of a three-digit one.
    [Fact]
    public void Cards_Num_SortsNumericallyAcrossWidths()
    {
        string[] paths =
        [
            "docs/jobs/queue/1000-A-x.md",
            "docs/jobs/queue/999-B-x.md",
            "docs/jobs/queue/40-A-x.md",
            "docs/jobs/queue/007-A-x.md",
        ];
        var ids = paths
            .Select(p => CardFiles.FromPath(Jobs, p) ?? throw new InvalidOperationException($"not a card: {p}"))
            .OrderBy(c => c.Num)
            .Select(c => c.Id);
        Assert.Equal(["007-A", "40-A", "999-B", "1000-A"], ids);
    }

    // CARD-4 (931-A): a number too big to be a card number is no card; the file is ignored, as any
    // other file in the folder is.
    [Fact]
    public void Cards_Num_TooBigForANumber_Ignored()
    {
        Assert.Null(CardFiles.FromPath(Jobs, "docs/jobs/queue/99999999999999999999999-A-x.md"));
        Assert.Null(CardFiles.Parse(Jobs, "docs/jobs/queue/99999999999999999999999-A-x.md", "Status: ready"));
    }

    // 969-A: the header's Category: and its old name Category: are the same field.
    [Fact]
    public void Card_CategoryAndCategoryHeader_ParseTheSame()
    {
        var old = ParseQueue("010-B-x.md", "# t\n\nLane: B\nStatus: ready\n");
        var now = ParseQueue("010-B-x.md", "# t\n\nCategory: B\nStatus: ready\n");

        Assert.Equal("B", old.HeaderCategory);
        Assert.Equal(old.HeaderCategory, now.HeaderCategory);
        Assert.Equal(old.Category, now.Category);
        Assert.Equal(old.Status, now.Status);
    }
}
