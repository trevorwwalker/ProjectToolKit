// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;

namespace Scheduler.Core.Tests.Pool;

/// <summary>
/// The multi-project pool's choice of project when a worker comes free (spec B1 MP-2 to MP-7).
/// Test plan: scheduler-next/test-plans/weighted-effort.md. Two projects, P (weight 70) and O
/// (weight 30). The share rule and the order of resumes and priority follow spec B3 Q1 to Q5 as
/// answered on card 300-A; the tests given a share rule as a parameter check that it is never
/// asked when at most one project can use the worker.
/// </summary>
public class PoolTests
{
    private static Card Card(CardFolder folder, string name, string status = "ready", int prio = 1, string? worker = null, params string[] depends)
    {
        var parts = name.Split('-');
        return new Card
        {
            Folder = folder,
            Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Status = status,
            Prio = prio,
            WorkerReq = worker,
            Depends = depends,
        };
    }

    private static DispatchPlan Plan(params Card[] cards) =>
        DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2);

    private static PoolProject P(params Card[] cards) => new("P", 70, Plan(cards));

    private static PoolProject O(params Card[] cards) => new("O", 30, Plan(cards));
    /// <summary>A type as dispatch sees it, roomy enough for one start (WT-2). Categories are letters, "B,C" as "BC".</summary>
    private static DispatchType Worker(string name = "w1", string model = "", string categories = "", params string[] projects) =>
        new() { Name = name, Model = model, Max = 9, Projects = projects, IsDefault = true }; // 1146-A: takes cards with no Worker: line

    private static ServerCache Servers() => new(new UpProbe(), new FixedClock(), new NoLog());

    /// <summary>WT-7 with one place free (maxWorkers 1): the card the pool starts, and on which type, or null.</summary>
    private static PoolOffer? Pick(IReadOnlyList<PoolProject> projects, DispatchType type, IEnumerable<PoolRun>? runs = null) =>
        PoolDispatcher.Dispatch(projects, new Dictionary<string, SchedulerState>(), 1, 0, [type], false, new AgentQuota(), Servers(),
            runs ?? [], Clock, _ => new Dictionary<string, int>(), _ => true).FirstOrDefault();

    // MP-3, WRK-7: threads per worker are cores over the pool's size, not over one project's slots.
    [Fact]
    public void Pool_ThreadsBudget_CoresOverPoolSize()
    {
        // 16 cores; 4 workers across two projects (P has 3 slots' worth of cards, O 1).
        Assert.Equal(4, PoolDispatcher.ThreadsPerWorker(16, 4));

        // Whatever the project: the budget does not depend on how the work is split.
        Assert.Equal(PoolDispatcher.ThreadsPerWorker(16, 4), PoolDispatcher.ThreadsPerWorker(16, 3 + 1));

        // As WRK-7 today: at least one thread, and an empty pool counts as one worker.
        Assert.Equal(1, PoolDispatcher.ThreadsPerWorker(2, 4));
        Assert.Equal(16, PoolDispatcher.ThreadsPerWorker(16, 0));
    }

    // MP-4: each project has a weight, read from the top-level config. A missing weight waits on
    // spec B3 Q2, so the parser leaves such a project out of the weights and the caller decides.
    [Fact]
    public void Pool_ProjectWeights_Parsed()
    {
        const string json = """
            { "projects": [
                { "name": "AmigaX", "repo": "C:/Projects/AmigaX", "weight": 70 },
                { "name": "Other", "repo": "C:/Projects/Other", "weight": "30" }
            ] }
            """;
        var weights = PoolWeights.Parse(json);
        Assert.Equal(70, weights["AmigaX"]);
        Assert.Equal(30, weights["other"]);
        Assert.Equal(2, weights.Count);

        // Spec B3 Q5: a missing weight is the same as the smallest other project's; with no
        // weight written anywhere, every project weighs the same.
        const string missing = """
            { "projects": [
                { "name": "A", "weight": 70 }, { "name": "B", "weight": 30 }, { "name": "C" }
            ] }
            """;
        Assert.Equal(30, PoolWeights.Parse(missing)["C"]);
        var none = PoolWeights.Parse("""{ "projects": [ { "name": "A" }, { "name": "B" } ] }""");
        Assert.Equal(none["A"], none["B"]);
        Assert.True(none["A"] > 0);

        Assert.Empty(PoolWeights.Parse(null));
        Assert.Empty(PoolWeights.Parse("{ }"));
    }

    // MP-5: P is far below its share, but its only ready card asks for a worker the free one is
    // not, or depends on an unfinished card; O has a runnable card, so O gets the worker.
    [Fact]
    public void Pool_OnlyProjectsWithRunnableCardTakePart()
    {
        var o = O(Card(CardFolder.Queue, "010-A-o"));

        var wrongWorker = P(Card(CardFolder.Queue, "010-A-p", worker: "big"));
        var chosen = Pick([wrongWorker, o], Worker("small"));
        Assert.NotNull(chosen);
        Assert.Equal("O", chosen.Project);
        Assert.Equal("010-A", chosen.Candidate.Card.Id);

        var unfinishedDep = P(Card(CardFolder.Queue, "020-A-p", depends: "010-A"), Card(CardFolder.Queue, "010-A-x", "blocked on hardware"));
        chosen = Pick([unfinishedDep, o], Worker("small"));
        Assert.Equal("O", chosen?.Project);

        // P takes no part at all: only O is offered.
        Assert.Equal("O", Pick([wrongWorker, o], Worker("small"))?.Project);
    }

    // MP-5: no project has a card the worker can run: no start, no error.
    [Fact]
    public void Pool_NoProjectHasRunnableCard_WorkerStaysIdle()
    {
        Assert.Null(Pick([P(), O()], Worker()));

        var blockedP = P(Card(CardFolder.Queue, "010-A-p", "blocked on hardware"));
        var blockedO = O(Card(CardFolder.Queue, "020-A-o", depends: "010-A"));
        Assert.Null(Pick([blockedP, blockedO], Worker()));
        Assert.Null(Pick([], Worker()));
    }

    // MP-6: O has no cards for the whole window and P has used every minute; O's share goes to
    // P, which keeps getting every free worker. The share rule is not even asked.
    [Fact]
    public void Pool_IdleProjectShare_GoesToOthers()
    {
        var p = P(Card(CardFolder.Queue, "010-A-p1"), Card(CardFolder.Queue, "020-B-p2"));
        var first = Pick([p, O()], Worker());
        Assert.Equal("P", first?.Project);
        Assert.Equal("010-A", first?.Candidate.Card.Id);

        var p2 = P(Card(CardFolder.Queue, "020-B-p2"));
        var second = Pick([p2, O()], Worker());
        Assert.Equal("P", second?.Project);
        Assert.Equal("020-B", second?.Candidate.Card.Id);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly IClock Clock = new At(Now);

    /// <summary>A run of <paramref name="project"/>'s card, started and (unless still running) ended so many minutes before now.</summary>
    private static PoolRun Run(string project, double startedAgo, double? endedAgo = null) =>
        new(project, Now.AddMinutes(-startedAgo), endedAgo is double e ? Now.AddMinutes(-e) : null);

    // MP-2, spec B3 Q4: the pool's workers are one list in the top-level config, each naming the
    // projects it serves (none named: every project). One free worker; only O has a ready card.
    [Fact]
    public void Pool_AnyWorkerTakesAnyProjectsCard()
    {
        PoolProject[] projects = [P(), O(Card(CardFolder.Queue, "010-A-o"))];

        var chosen = Pick(projects, Worker(), []);
        Assert.Equal("O", chosen?.Project);
        Assert.Equal("010-A", chosen?.Candidate.Card.Id);

        // A worker naming both projects takes it too (names case-insensitive, G-1) ...
        Assert.Equal("O", Pick(projects, Worker(projects: ["p", "o"]))?.Project);

        // ... one serving only P does not: it stays idle.
        Assert.Null(Pick(projects, Worker(projects: ["P"])));

        // With both projects ready, a worker serving only O never takes P's card, however far below its share P is.
        PoolProject[] both = [P(Card(CardFolder.Queue, "010-A-p")), O(Card(CardFolder.Queue, "010-A-o"))];
        Assert.Equal("O", Pick(both, Worker(projects: ["O"]), [Run("O", 120)])?.Project);
    }

    // MP-6, spec B3 Q2: over the last 2 h P used 60 worker-minutes and O 60, so P has 50 % of the
    // use against 70 % of the weight (20 points below its share) and O 50 % against 30 % (20
    // above). Running workers count up to now; the share is weight share minus used share.
    [Fact]
    public void Pool_FurthestBelowShare_GetsWorker()
    {
        PoolProject[] projects = [P(Card(CardFolder.Queue, "010-A-p")), O(Card(CardFolder.Queue, "010-A-o"))];
        PoolRun[] runs = [Run("P", 100, 40), Run("O", 90, 60), Run("O", 30)];

        Assert.Equal(60, PoolShares.Minutes(runs, "P", Now));
        Assert.Equal(60, PoolShares.Minutes(runs, "O", Now));
        Assert.Equal("P", Pick(projects, Worker(), runs)?.Project);

        // P 90, O 30: P is 5 points over its share (75 % used), O 5 under, so O gets it.
        Assert.Equal("O", Pick(projects, Worker(), [Run("P", 90, 0), Run("O", 30)])?.Project);

        // P 70, O 30: both exactly on their share, a tie; it goes to the project listed first.
        PoolRun[] even = [Run("P", 70, 0), Run("O", 30, 0)];
        Assert.Equal("P", Pick(projects, Worker(), even)?.Project);
        Assert.Equal("O", Pick([projects[1], projects[0]], Worker(), even)?.Project);
    }

    // MP-6, spec B3 Q2: O gets its first card after 2 h of P-only work: a project with no minutes
    // is below its share and the other is over it, so O gets the next free worker.
    [Fact]
    public void Pool_ProjectWithNoMinutes_WinsOutright()
    {
        PoolProject[] projects = [P(Card(CardFolder.Queue, "020-A-p")), O(Card(CardFolder.Queue, "010-A-o"))];
        PoolRun[] runs = [Run("P", 120), Run("P", 120, 10)];

        var chosen = Pick(projects, Worker(), runs);
        Assert.Equal("O", chosen?.Project);
        Assert.Equal("010-A", chosen?.Candidate.Card.Id);

        // Nobody has any minutes yet: a tie, so the project listed first.
        Assert.Equal("P", Pick(projects, Worker(), [])?.Project);
    }

    // MP-6, spec B3 Q2: the window is the last 2 h. P's minutes all older than that count as 0;
    // a run across the window's start counts only its part inside.
    [Fact]
    public void Pool_WindowSlides()
    {
        PoolRun[] runs = [Run("P", 300, 121), Run("P", 125, 120), Run("O", 20, 0)];
        Assert.Equal(0, PoolShares.Minutes(runs, "P", Now));
        Assert.Equal(20, PoolShares.Minutes(runs, "O", Now));

        PoolProject[] projects = [O(Card(CardFolder.Queue, "010-A-o")), P(Card(CardFolder.Queue, "010-A-p"))];
        Assert.Equal("P", Pick(projects, Worker(), runs)?.Project);

        // A run that began 150 minutes ago and ended 90 ago counts its last 30.
        Assert.Equal(30, PoolShares.Minutes([Run("P", 150, 90)], "P", Now));

        // An hour earlier the window ran from 180 to 60 minutes ago: P had 59 + 5 minutes in it.
        Assert.Equal(64, PoolShares.Minutes([Run("P", 300, 121), Run("P", 125, 120)], "P", Now.AddMinutes(-60)));
    }

    // MP-6, MP-7, DISP-14, spec B3 Q1: the weights pick the project; priority only orders the
    // cards inside it. O is over its share with a Priority: high card; P under with a normal one.
    [Fact]
    public void Pool_HighPriorityCard_VsWeights()
    {
        var o = O(Card(CardFolder.Queue, "020-A-normal"), Card(CardFolder.Queue, "030-A-urgent", prio: 0));
        PoolProject[] projects = [P(Card(CardFolder.Queue, "010-A-p")), o];
        PoolRun[] runs = [Run("O", 60, 0)];

        var chosen = Pick(projects, Worker(), runs);
        Assert.Equal("P", chosen?.Project);
        Assert.Equal("010-A", chosen?.Candidate.Card.Id);

        // When O's turn comes (P has no card), its high-priority card goes first within O.
        Assert.Equal("030-A", Pick([P(), o], Worker(), runs)?.Candidate.Card.Id);
    }

    // MP-7, DISP-2, spec B3 Q3: a card left half-done by a stopped worker is resumed ahead of the
    // weights. O is over its share with an orphan in active/; P under with a queued card.
    [Fact]
    public void Pool_OrphanResume_VsWeights()
    {
        var o = O(Card(CardFolder.Active, "030-A-orphan"));
        var p = P(Card(CardFolder.Queue, "010-A-p"));
        PoolRun[] runs = [Run("O", 60, 0)];

        var chosen = Pick([p, o], Worker(), runs);
        Assert.Equal("O", chosen?.Project);
        Assert.Equal("030-A", chosen?.Candidate.Card.Id);
        Assert.True(chosen?.Candidate.Resume);

        // With an orphan in each project, the shares pick between them: P is below its share.
        var pOrphan = P(Card(CardFolder.Active, "040-B-porphan"));
        chosen = Pick([o, pOrphan], Worker(), runs);
        Assert.Equal("P", chosen?.Project);
        Assert.True(chosen?.Candidate.Resume);
    }

    // MP-7, DISP-14 with WT-3 to WT-6: once P is chosen, the card is the one single-project dispatch
    // would give the same free worker: Prio, then orphans first, then Num and Category; and only a
    // card the worker fits.
    [Fact]
    public void Pool_WithinProject_SelectionAsToday()
    {
        Card[] cards =
        [
            Card(CardFolder.Queue, "020-B-b"),
            Card(CardFolder.Queue, "010-C-c", prio: 2),
            Card(CardFolder.Queue, "010-A-a"),
            Card(CardFolder.Queue, "015-D-d", prio: 0),
            Card(CardFolder.Active, "030-E-orphan"),
            Card(CardFolder.Queue, "005-F-big", worker: "big"),
        ];
        (string Name, string Categories)[] workers = [("w1", ""), ("w1", "B,C"), ("w1", "E"), ("big", ""), ("w1", "Z")];

        foreach (var count in Enumerable.Range(1, cards.Length))
        {
            var plan = Plan(cards[..count]);
            foreach (var worker in workers)
            {
                var today = SingleProjectPick(plan, Worker(worker.Name, categories: worker.Categories));
                var pooled = Pick([new PoolProject("P", 70, plan)], Worker(worker.Name, categories: worker.Categories));
                Assert.Equal(today?.Card.Id, pooled?.Candidate.Card.Id);
                Assert.Equal(today?.Resume, pooled?.Candidate.Resume);
            }
        }

        // The full set to the any-category worker: 015-D (Prio 0) first, as DISP-14 orders it.
        var all = Pick([new PoolProject("P", 70, Plan(cards))], Worker());
        Assert.Equal("015-D", all?.Candidate.Card.Id);
    }

    /// <summary>What single-project dispatch starts on one free worker of that type (WT-5, WT-6).</summary>
    private static DispatchCandidate? SingleProjectPick(DispatchPlan plan, DispatchType worker)
    {
        DispatchCandidate? picked = null;
        TypeDispatcher.Dispatch(plan, SchedulerState.Running, 1, 0, [worker with { Max = 1 }], false, new AgentQuota(),
            Servers(), new Dictionary<string, int>(), s =>
            {
                picked ??= s.Candidate;
                return true;
            });
        return picked;
    }

    private sealed class UpProbe : IServerProbe
    {
        public bool IsUp(string endpoint) => true;

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }

    private sealed class NoLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    private sealed class At(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch;
    }
}
