using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;

namespace Scheduler.Core.Tests.Pool;

/// <summary>
/// 1055-C: the pool's queue in the order the pool will start it (<see cref="PoolQueueOrder"/>): each
/// project's own queue merged by the dispatcher's rule - weights over the window, free workspaces,
/// type caps and the total - with each project's own order kept.
/// </summary>
public class PoolQueueOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TwoProjectsWeighted75To25InterleaveAboutThreeToOne()
    {
        var a = Lane("A", 75, Cards(12));
        var b = Lane("B", 25, Cards(12));

        var order = Order([a, b], maxWorkers: 1);

        Assert.Equal(24, order.Count);
        var first16 = order.Take(16).Count(e => e.Project == "A");
        Assert.InRange(first16, 11, 13);
        Assert.Equal("A", order[0].Project);   // nobody has minutes: the bigger weight first
        Assert.Contains(order.Take(4), e => e.Project == "B");   // B is not starved behind all of A
    }

    [Fact]
    public void AProjectWithNoFreeWorkspaceDropsBack()
    {
        // A outweighs B by far but its one workspace is busy: B's card goes first, A's once its worker ends.
        var a = Lane("A", 90, Cards(2), workspaces: 1, running: [new PoolQueueWorker("100-Z", "w", "Z", Now.AddMinutes(-10))]);
        var b = Lane("B", 10, Cards(1));

        var order = Order([a, b], maxWorkers: 2);

        Assert.Equal(["B:1-Q", "A:1-Q", "A:2-R"], order.Select(e => $"{e.Project}:{e.Card}"));
    }

    [Fact]
    public void ADependencyIsNeverPlacedBeforeItsParent()
    {
        // A's 2-Y depends on 1-X: it waits until 1-X has ended, so B's cards fill the places meanwhile.
        var a = Lane("A", 50, [Card("1-X"), Card("2-Y", "1-X")]);
        var b = Lane("B", 50, [Card("3-P"), Card("4-Q")]);

        var order = Order([a, b], maxWorkers: 3).Select(e => e.Card).ToList();

        Assert.Equal(["1-X", "3-P", "4-Q", "2-Y"], order);
        Assert.True(order.IndexOf("2-Y") > order.IndexOf("1-X"));
    }

    [Fact]
    public void OneProjectIsItsOwnOrder()
    {
        var own = new[] { Card("7-C"), Card("3-A"), Card("9-B"), Card("4-C"), Card("5-D", "9-B") };

        var order = Order([Lane("A", 70, own)], maxWorkers: 2);

        Assert.Equal(own.Select(c => c.Name), order.Select(e => e.Card));
        Assert.All(order, e => Assert.Equal("A", e.Project));
    }

    [Fact]
    public void AFullTypeHoldsItsCardsUntilAWorkerOfItEnds()
    {
        // One type with room for one: the pool's total of 3 never runs more than one at a time.
        var a = Lane("A", 50, Cards(2));
        var b = Lane("B", 50, Cards(2));

        var order = PoolQueueOrder.Order(
            [a, b], new Dictionary<string, SchedulerState>(), 3, [PoolFakes.Type("w", max: 1)], false, new AgentQuota(), PoolFakes.Servers(), [], Now);

        Assert.Equal(["A", "B", "A", "B"], order.Select(e => e.Project));
    }

    [Fact]
    public void ACardNoTypeTakesAndAStoppedProjectGoLastInTheirOwnOrder()
    {
        var a = Lane("A", 50, Cards(2));
        var b = Lane("B", 50, Cards(2));
        var states = new Dictionary<string, SchedulerState> { ["A"] = SchedulerState.Paused };

        var order = PoolQueueOrder.Order(
            [a, b], states, 2, [PoolFakes.Type("w")], false, new AgentQuota(), PoolFakes.Servers(), [], Now);

        Assert.Equal(["B:1-Q", "B:2-R", "A:1-Q", "A:2-R"], order.Select(e => $"{e.Project}:{e.Card}"));
    }

    [Fact]
    public void TheTypicalRunIsTheWindowsMeanFinishedRun()
    {
        Assert.Equal(PoolQueueOrder.DefaultRun, PoolQueueOrder.TypicalRun([]));
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            PoolQueueOrder.TypicalRun([new PoolRun("A", Now.AddMinutes(-40), Now.AddMinutes(-30)), new PoolRun("B", Now.AddMinutes(-30), Now), new PoolRun("A", Now, null)]));
    }

    private static List<PoolQueueEntry> Order(IReadOnlyList<PoolQueueLane> lanes, int maxWorkers) =>
        [.. PoolQueueOrder.Order(lanes, new Dictionary<string, SchedulerState>(), maxWorkers, [PoolFakes.Type("w")], false, new AgentQuota(), PoolFakes.Servers(), [], Now)];

    private static PoolQueueLane Lane(string name, int weight, IReadOnlyList<Card> queue, int? workspaces = null, IReadOnlyList<PoolQueueWorker>? running = null) =>
        new(new PoolProject(name, weight, Plan()) { Workspaces = workspaces, Busy = running?.Count ?? 0 }, queue) { Running = running ?? [] };

    // n cards of distinct categories (Q, R, S, ...), so the category rule does not hold any back.
    private static Card[] Cards(int n) =>
        [.. Enumerable.Range(1, n).Select(i => Card($"{i}-{(char)('P' + i)}"))];

    private static Card Card(string id, params string[] depends)
    {
        var parts = id.Split('-');
        return new Card
        {
            Folder = CardFolder.Queue,
            Path = $"jobs/queue/{id}.md",
            Name = id,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = id,
            Status = "ready",
            Depends = depends,
        };
    }

    private static DispatchPlan Plan() => DispatchPlanner.Plan([], [], new Dictionary<string, int>(), null, 2);
}
