// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// 1111-B (planner-outbox.md "Card numbers"): a card named <c>new-{L}-{slug}.md</c> asks the scheduler for a
/// number; the counter is the caller's, so these give it one.
/// </summary>
public class CardNumbersTests
{
    private const string Jobs = "docs/jobs";

    private static Func<long> Counter(long last) => () => ++last;

    [Fact]
    public void NewFromPath_NewNameInACardFolder_IsANewCard()
    {
        var card = CardNumbers.NewFromPath(Jobs, "docs/jobs/queue/new-b-hands-out-numbers.md");
        Assert.NotNull(card);
        Assert.Equal("B", card.Category);
        Assert.Equal("hands-out-numbers", card.Slug);
        Assert.Equal("new-B-hands-out-numbers", card.Name);
        Assert.Null(CardNumbers.NewFromPath(Jobs, "docs/jobs/queue/12-B-x.md"));
        Assert.Null(CardNumbers.NewFromPath(Jobs, "docs/jobs/plans/new-B-x.md"));
        Assert.Null(CardNumbers.NewFromPath(Jobs, "other/queue/new-B-x.md"));
    }

    // A new-* card is not a card (CARD-4), so the tick never sees it and never dispatches it.
    [Fact]
    public void NewCard_IsNotACard_SoNeverDispatched()
    {
        Assert.Null(CardFiles.FromPath(Jobs, "docs/jobs/queue/new-B-x.md"));
        Assert.Null(CardFiles.Parse(Jobs, "docs/jobs/queue/new-B-x.md", "# new-B: x\n\nStatus: ready\n"));
    }

    [Fact]
    public void Highest_EveryFolderIncludingArchive()
    {
        string[] paths =
        [
            "docs/jobs/queue/12-B-x.md", "docs/jobs/active/30-A-y.md", "docs/jobs/review/7-C-z.md",
            "docs/jobs/done/99-B-d.md", "docs/jobs/archive/1200-A-old.md", "docs/jobs/plans/5000-A-plan.md",
            "docs/jobs/queue/new-B-n.md", "docs/jobs/requests/9999-x.md",
        ];
        Assert.Equal(1200, CardNumbers.Highest(Jobs, paths));
        Assert.Equal(0, CardNumbers.Highest(Jobs, []));
    }

    [Fact]
    public void Number_TwoBatchesOneCounter_GetDistinctNumbers()
    {
        var next = Counter(1200);
        var first = CardNumbers.Number(Jobs, [KeyValuePair.Create("docs/jobs/queue/new-B-a.md", "# new-B: a\n")], next);
        var second = CardNumbers.Number(Jobs, [KeyValuePair.Create("docs/jobs/queue/new-B-a.md", "# new-B: a again\n")], next);
        Assert.Equal(1201, first.Numbered.Single().Num);
        Assert.Equal(1202, second.Numbered.Single().Num);
        Assert.Equal("docs/jobs/queue/1201-B-a.md", first.Numbered.Single().To);
        Assert.Equal("docs/jobs/queue/1202-B-a.md", second.Numbered.Single().To);
    }

    [Fact]
    public void Number_TitleDependsOnAndPathsAreRewritten_UnknownNamesLeft()
    {
        var files = new[]
        {
            KeyValuePair.Create("docs/jobs/queue/new-B-a.md", "# new-B: first\n\nCategory: B\nDepends on: none\n"),
            KeyValuePair.Create("docs/jobs/queue/new-B-a-b.md", "# new-B-a-b: second\n\nDepends on: new-B-a, new-C-elsewhere\n\nSee jobs/queue/new-B-a.md.\n"),
            KeyValuePair.Create("docs/jobs/active/40-A-x.md", "# 40-A: x\n\nAfter new-B-a-b lands.\n"),
            KeyValuePair.Create("docs/jobs/active/41-A-y.md", "# 41-A: y\n"),
        };
        var result = CardNumbers.Number(Jobs, files, Counter(50));

        Assert.Equal(["docs/jobs/queue/51-B-a.md", "docs/jobs/queue/52-B-a-b.md"], result.Numbered.Select(n => n.To));
        Assert.Equal("# 51-B: first\n\nCategory: B\nDepends on: none\n", result.Texts["docs/jobs/queue/51-B-a.md"]);
        Assert.Equal("# 52-B: second\n\nDepends on: 51-B, new-C-elsewhere\n\nSee jobs/queue/51-B-a.md.\n", result.Texts["docs/jobs/queue/52-B-a-b.md"]);
        Assert.Equal("# 40-A: x\n\nAfter 52-B lands.\n", result.Texts["docs/jobs/active/40-A-x.md"]);
        Assert.False(result.Texts.ContainsKey("docs/jobs/active/41-A-y.md"));   // unchanged files are left out
        Assert.Equal(["new-C-elsewhere"], result.Unknown);
    }

    [Fact]
    public void Number_Change_WritesAtTheNumberedPath()
    {
        var change = new JobsChange
        {
            Message = "jobs: card",
            Writes = new Dictionary<string, string>
            {
                ["docs/jobs/queue/new-A-x.md"] = "# new-A: x\n",
                ["docs/jobs/queue/3-A-y.md"] = "# 3-A: y\n\nDepends on: new-A-x\n",
            },
            Moves = [new FileMove { From = "docs/jobs/review/2-A-z.md", To = "docs/jobs/done/2-A-z.md" }],
        };
        var (numbered, files) = CardNumbers.Number(change, Jobs, Counter(9));
        Assert.Equal("docs/jobs/queue/10-A-x.md", files.Numbered.Single().To);
        Assert.Equal(["docs/jobs/queue/10-A-x.md", "docs/jobs/queue/3-A-y.md"], numbered.Writes.Keys);
        Assert.Equal("# 10-A: x\n", numbered.Writes["docs/jobs/queue/10-A-x.md"]);
        Assert.Equal("# 3-A: y\n\nDepends on: 10-A\n", numbered.Writes["docs/jobs/queue/3-A-y.md"]);
        Assert.Equal(change.Moves, numbered.Moves);
        Assert.Equal("jobs: card", numbered.Message);

        var plain = new JobsChange { Message = "m", Writes = new Dictionary<string, string> { ["docs/jobs/queue/3-A-y.md"] = "# 3-A\n" } };
        Assert.Same(plain, CardNumbers.Number(plain, Jobs, Counter(9)).Change);
    }

    [Fact]
    public void OnMain_MovesWritesAndNamesTheNumbers()
    {
        var (change, files) = CardNumbers.OnMain(Jobs,
            [KeyValuePair.Create("docs/jobs/queue/new-B-a.md", "# new-B: a\n"), KeyValuePair.Create("docs/jobs/queue/new-C-b.md", "# new-C: b\n")],
            [KeyValuePair.Create("docs/jobs/queue/5-B-q.md", "# 5-B: q\n\nDepends on: new-C-b\n")],
            Counter(700));
        Assert.Equal("jobs: number new-B-a as 701, new-C-b as 702", change.Message);
        Assert.Equal([("docs/jobs/queue/new-B-a.md", "docs/jobs/queue/701-B-a.md"), ("docs/jobs/queue/new-C-b.md", "docs/jobs/queue/702-C-b.md")],
            change.Moves.Select(m => (m.From, m.To)));
        Assert.Equal("# 702-C: b\n", change.Writes["docs/jobs/queue/702-C-b.md"]);
        Assert.Equal("# 5-B: q\n\nDepends on: 702-C\n", change.Writes["docs/jobs/queue/5-B-q.md"]);
        Assert.Equal("card numbered: Toolkit/new-B-a -> 701-B-a", CardNumbers.LogLine("Toolkit", files.Numbered[0]));
        Assert.Equal("card numbered: new-B-a -> 701-B-a", CardNumbers.LogLine("", files.Numbered[0]));
    }
}
