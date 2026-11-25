// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Accounting;

public class CardStatsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(-8));

    private static RunStat Run(string card, string result, int minutes, string model, DateTimeOffset finished) =>
        new() { Finished = finished, Result = result, Minutes = minutes, Model = model, Card = card };

    private static Card Card(string id, CardFolder folder, string status = "ready") => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{id}-slug.md",
        Name = $"{id}-slug",
        Num = long.Parse(id.AsSpan(0, id.IndexOf('-')), System.Globalization.CultureInfo.InvariantCulture), // 931-A: any width
        Category = id.AsSpan(4).ToString(),
        Id = id,
        Status = status,
    };

    // ACCT-6 (Card-Stats L891, Stats-Text-Of L913): the last 24 h by card, with the runs that
    // were nobody's fault left out.
    [Fact]
    public void Acct_CardStats_24hByCardExclusions()
    {
        var runs = new[]
        {
            Run("010-A", "done", 30, "opus", Now.AddHours(-1)),
            Run("010-A", "done", 20, "opus", Now.AddHours(-2)),
            Run("020-B", "review", 15, "opus", Now.AddHours(-3)),
            Run("030-C", "ended early", 5, "opus", Now.AddHours(-4)),
            Run("040-D", "crashed", 5, "opus", Now.AddHours(-5)),
            Run("050-E", "no log", 1, "opus", Now.AddHours(-6)),
            Run("060-F", "out of quota", 1, "opus", Now.AddHours(-6)),
            Run("070-G", "model server down", 1, "opus", Now.AddHours(-6)),
            Run("080-H", "done", 90, "opus", Now.AddHours(-25)),
            Run("", "done", 10, "opus", Now.AddHours(-1)),
        };
        var cards = new[]
        {
            Card("010-A", CardFolder.Done),
            Card("020-B", CardFolder.Review),
            Card("030-C", CardFolder.Active, "needs-human: Q1 parked? [yes -> done | no -> queue]"),
            Card("040-D", CardFolder.Queue),
            Card("080-H", CardFolder.Done),
        };

        var entries = CardStats.GroupByCard(runs, cards, Now);

        // Four cards and five runs: the three excluded results, the run outside the window and
        // the run with no card are all left out.
        Assert.Equal(4, entries.Count);
        // Two finished cards in the window: 2/24 = 0.08 cards/hr (ACCT-6, two decimals per 130-C Q1).
        // ACCT-6's wording as 520-A changed it (the developer, 2026-11-18): no percentage after the
        // done count.
        Assert.Equal("4 cards (5 runs): done 1, 1 in review, 1 waiting on questions, 1 open; 0.08 cards/hr",
            CardStats.StatsText(entries));
        Assert.DoesNotContain("%", CardStats.StatsText(entries), StringComparison.Ordinal);
        Assert.Equal("1 h 15 min", CardStats.RunTimeOf(entries));
        var counted = entries.Select(e => e.Card).ToList();
        foreach (var excluded in new[] { "050-E", "060-F", "070-G", "080-H" })
        {
            Assert.DoesNotContain(excluded, counted);
        }

        // Runs are counted per card, and the state follows where the card is now.
        Assert.Equal(2, entries.Single(e => e.Card == "010-A").Runs);
        Assert.Equal(CardRunState.Done, entries.Single(e => e.Card == "010-A").State);
        Assert.Equal(CardRunState.Review, entries.Single(e => e.Card == "020-B").State);
        Assert.Equal(CardRunState.Questions, entries.Single(e => e.Card == "030-C").State);
        Assert.Equal(CardRunState.Open, entries.Single(e => e.Card == "040-D").State);

        // A card that is back in the queue is open; one parked on the planner is a question.
        var back = CardStats.GroupByCard(
            [Run("041-E", "ended early", 5, "opus", Now.AddHours(-1))],
            [Card("041-E", CardFolder.Queue)], Now);
        Assert.Equal(CardRunState.Open, back.Single().State);
        var planner = CardStats.GroupByCard(
            [Run("042-F", "ended early", 5, "opus", Now.AddHours(-1))],
            [Card("042-F", CardFolder.Active, "waiting: planner (request #3)")], Now);
        Assert.Equal(CardRunState.Questions, planner.Single().State);
        var dev = CardStats.GroupByCard(
            [Run("043-G", "ended early", 5, "opus", Now.AddHours(-1))],
            [Card("043-G", CardFolder.Queue, "waiting: developer - Q1")], Now);
        Assert.Equal(CardRunState.Questions, dev.Single().State);

        // Nothing in the window says so.
        Assert.Equal("", CardStats.StatsText([]));
        Assert.Equal(new[] { "Last 24 h: no runs finished." }, CardStats.Summary([], [], Now));
    }

    // ACCT-6 (Run-Stats-Html L929): each card sits under the model of its latest run, and the
    // per-model lines appear only when more than one model ran.
    [Fact]
    public void Acct_CardStats_PerModelLines_WhenTwoModels()
    {
        var runs = new[]
        {
            Run("010-A", "done", 30, "x", Now.AddHours(-2)),
            Run("010-A", "done", 30, "y", Now.AddHours(-1)),
            Run("020-B", "done", 10, "x", Now.AddHours(-1)),
        };
        var cards = new[] { Card("010-A", CardFolder.Done), Card("020-B", CardFolder.Done) };

        // The card that ran on both models is counted under the later one only.
        var entries = CardStats.GroupByCard(runs, cards, Now);
        Assert.Equal("y", entries.Single(e => e.Card == "010-A").Model);
        Assert.Equal("x", entries.Single(e => e.Card == "020-B").Model);

        var lines = CardStats.Summary(runs, cards, Now);
        Assert.Equal(3, lines.Count);
        Assert.Equal("Last 24 h (1 h 10 min): 2 cards (3 runs): done 2, 0 in review, 0 waiting on questions, 0 open; 0.08 cards/hr",
            lines[0]);
        Assert.Equal("  x (10 min): 1 cards (1 runs): done 1, 0 in review, 0 waiting on questions, 0 open; 0.04 cards/hr",
            lines[1]);
        Assert.Equal("  y (1 h 0 min): 1 cards (2 runs): done 1, 0 in review, 0 waiting on questions, 0 open; 0.04 cards/hr",
            lines[2]);

        // A models.json short name is shown instead of the key.
        var shortNames = new[] { new ModelEntry { Key = "x", Short = "X small" }, new ModelEntry { Key = "y", Short = "Y big" } };
        var named = CardStats.Summary(runs, cards, Now, shortNames);
        Assert.Equal("  X small (10 min): 1 cards (1 runs): done 1, 0 in review, 0 waiting on questions, 0 open; 0.04 cards/hr",
            named[1]);

        // One model: no per-model lines.
        var one = CardStats.Summary(runs.Take(1).Append(runs[2]).ToArray(), cards, Now);
        Assert.Single(one);
        Assert.Equal("Last 24 h (40 min): 2 cards (2 runs): done 2, 0 in review, 0 waiting on questions, 0 open; 0.08 cards/hr",
            one[0]);
    }
}
