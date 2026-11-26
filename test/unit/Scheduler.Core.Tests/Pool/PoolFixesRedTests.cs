// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;

namespace Scheduler.Core.Tests.Pool;

/// <summary>1472-A: a paused-red project still offers its <c>Fixes-red</c> cards, and only those; no other state does.</summary>
public class PoolFixesRedTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private static Card Card(string name, bool fixesRed, params string[] depends)
    {
        var parts = name.Split('-');
        return new Card
        {
            Folder = CardFolder.Queue,
            Path = $"jobs/queue/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Status = "ready",
            Prio = 1,
            FixesRed = fixesRed,
            Depends = depends,
        };
    }

    private static DispatchPlan Plan(params Card[] cards) =>
        DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2);

    private static List<string> Started(DispatchPlan plan, SchedulerState state)
    {
        var ids = new List<string>();
        TypeDispatcher.Dispatch(plan, state, 9, 0, [PoolFakes.Type("w1")], false, new AgentQuota(), PoolFakes.Servers(),
            new Dictionary<string, int>(), s =>
            {
                ids.Add(s.Candidate.Card.Id);
                return true;
            });
        return ids;
    }

    [Fact]
    public void PausedRed_OffersOnlyTheFixesRedCard()
    {
        var plan = Plan(Card("1-A-plain", false), Card("2-B-fix", true));
        Assert.Equal(["2-B"], Started(plan, SchedulerState.PausedRed));
    }

    [Fact]
    public void PausedRed_FixesRedCardWithUnmetDependsOn_IsStillHeld()
    {
        var plan = Plan(Card("1-A-plain", false), Card("2-B-fix", true, "1-A"));
        Assert.Empty(Started(plan, SchedulerState.PausedRed));
    }

    [Theory]
    [InlineData(SchedulerState.Paused)]
    [InlineData(SchedulerState.Draining)]
    [InlineData(SchedulerState.PausedQuota)]
    public void OtherPausedStates_IgnoreTheHeader(SchedulerState state)
    {
        var plan = Plan(Card("1-A-plain", false), Card("2-B-fix", true));
        Assert.Empty(Started(plan, state));
    }

    [Fact]
    public void Running_StartsBothCards() =>
        Assert.Equal(2, Started(Plan(Card("1-A-plain", false), Card("2-B-fix", true)), SchedulerState.Running).Count);

    [Fact]
    public void PausedRed_TheOtherCardKeepsItsNote()
    {
        var plan = Plan(Card("1-A-plain", false), Card("2-B-fix", true));
        var result = TypeDispatcher.Dispatch(plan, SchedulerState.PausedRed, 9, 0, [PoolFakes.Type("w1")], false, new AgentQuota(),
            PoolFakes.Servers(), new Dictionary<string, int>(), _ => true);
        Assert.Contains(result.Notes, n => n.Note.Contains("dispatching is paused-red", StringComparison.Ordinal));
    }

    [Fact]
    public void Pool_OneProjectRed_OffersOnlyItsFixesRedCard_OtherUnaffected()
    {
        var red = new PoolProject("Red", 50, Plan(Card("1-A-plain", false), Card("2-B-fix", true)));
        var ok = new PoolProject("Ok", 50, Plan(Card("3-A-plain", false)));
        var states = new Dictionary<string, SchedulerState> { ["Red"] = SchedulerState.PausedRed, ["Ok"] = SchedulerState.Running };
        var started = PoolFakes.StartAll([red, ok], [PoolFakes.Type("w1")], Now, maxWorkers: 9, states: states);

        Assert.Equal(["2-B", "3-A"], started.Select(o => o.Candidate.Card.Id).Order());
    }

    [Fact]
    public void Pool_PausedProject_OffersNothingEvenWithTheHeader()
    {
        var p = new PoolProject("P", 50, Plan(Card("2-B-fix", true)));
        var states = new Dictionary<string, SchedulerState> { ["P"] = SchedulerState.Paused };
        Assert.Empty(PoolFakes.StartAll([p], [PoolFakes.Type("w1")], Now, maxWorkers: 9, states: states));
    }
}
