using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;
using static Scheduler.Core.Tests.Pool.PoolFakes;

namespace Scheduler.Core.Tests.Pool;

/// <summary>
/// WT-7 and WT-C9 (worker-types.md, card 910-A): the pool starts cards on worker types. A free place
/// in the total is offered to the cards, and the card's fit picks the type; the shares pick the project.
/// Projects P (weight 70) and O (weight 30).
/// </summary>
public class PoolTypesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static Card Card(CardFolder folder, string name)
    {
        var parts = name.Split('-');
        return new Card
        {
            Folder = folder,
            Path = $"jobs/{folder.ToString().ToLowerInvariant()}/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Status = "ready",
            Prio = 1,
        };
    }

    private static PoolProject Project(string name, int weight, params Card[] cards) =>
        new(name, weight, DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2));

    private static PoolRun Run(string project, double startedAgo, double? endedAgo = null) =>
        new(project, Now.AddMinutes(-startedAgo), endedAgo is double e ? Now.AddMinutes(-e) : null);

    // WT-1, WT-7: the total counts every project and adopted workers; the shares pick the project for
    // each place, and each start counts as a run for the next place of the same tick.
    [Fact]
    public void PoolTypes_TotalAcrossProjects_SharesPickProject()
    {
        var p = Project("P", 70, Card(CardFolder.Queue, "010-A-p1"), Card(CardFolder.Queue, "020-B-p2"), Card(CardFolder.Queue, "030-C-p3"));
        var o = Project("O", 30, Card(CardFolder.Queue, "010-A-o1"), Card(CardFolder.Queue, "020-B-o2"));
        DispatchType[] types = [Type("w1", max: 9)];

        // Each card has its own category, so their scopes do not overlap (WT-8). P has run 100 minutes and O none:
        // O is furthest below its share and offers first; a start this tick has run no time yet, so O keeps
        // the next place too until it has no card left, and P takes the last.
        var starts = StartAll([p, o], types, Now, maxWorkers: 3, runs: [Run("P", 100, 0)]);
        Assert.Equal(["O:010-A", "O:020-B", "P:010-A"], starts.Select(x => $"{x.Project}:{x.Candidate.Card.Id}"));

        // One worker (of any project) already runs: two places are left, not three.
        Assert.Equal(2, StartAll([p, o], types, Now, maxWorkers: 3, totalRunning: 1).Count);

        // The total is reached: nothing starts, whatever the type's own cap.
        Assert.Empty(StartAll([p, o], types, Now, maxWorkers: 3, totalRunning: 3));

        // A type's cap is its own: Max 1 stops after one start while the total has room.
        Assert.Single(StartAll([p, o], [Type("w1", max: 1)], Now, maxWorkers: 3));
        Assert.Single(StartAll([p, o], [Type("w1", max: 2, running: 1)], Now, maxWorkers: 3));
    }

    // WT-7, 300-A Q3: when any offer is a resume, only resumes take part, even though O is far above
    // its share and P far below.
    [Fact]
    public void PoolTypes_ResumeFirst_EvenWhenOtherProjectBelowShare()
    {
        var p = Project("P", 70, Card(CardFolder.Queue, "010-A-p"));
        var o = Project("O", 30, Card(CardFolder.Active, "030-A-orphan"));
        PoolRun[] runs = [Run("O", 60, 0)];

        var chosen = StartAll([p, o], [Type("w1")], Now, runs: runs).Single();
        Assert.Equal(("O", "030-A"), (chosen.Project, chosen.Candidate.Card.Id));
        Assert.True(chosen.Candidate.Resume);

        // With room for two, the resume goes first and P follows.
        Assert.Equal(["O", "P"], StartAll([p, o], [Type("w1")], Now, maxWorkers: 2, runs: runs).Select(x => x.Project));

        // A resume counts its retry before the start (DISP-16).
        var retries = new Dictionary<string, int>();
        PoolDispatcher.Dispatch([p, o], new Dictionary<string, SchedulerState>(), 1, 0, [Type("w1")], false, new AgentQuota(), Servers(),
            runs, new At(Now), project => project == "O" ? retries : new Dictionary<string, int>(), _ => true);
        Assert.Equal(1, retries["030-A"]);
    }

    // WT-3 (Q1), WT-7: a type that names projects serves only those; one that names none serves all.
    [Fact]
    public void PoolTypes_TypeServesOnlyItsProjects()
    {
        var p = Project("P", 70, Card(CardFolder.Queue, "010-A-p"));
        var o = Project("O", 30, Card(CardFolder.Queue, "010-A-o"));
        PoolRun[] runs = [Run("O", 120)];   // O is far over its share: P is next whenever it may be

        // A type serving only O never takes P card, however far below its share P is.
        var onlyO = Type("w1", projects: ["o"]);   // names match without case (G-1)
        Assert.Equal(["O"], StartAll([p, o], [onlyO], Now, maxWorkers: 2, runs: runs).Select(x => x.Project));

        // Serving only P, it leaves O waiting.
        Assert.Equal(["P"], StartAll([p, o], [Type("w1", projects: ["P"])], Now, maxWorkers: 2).Select(x => x.Project));

        // Two types: the card goes to the first listed one that serves its project.
        var starts = StartAll([p, o], [Type("forP", projects: ["P"]), Type("forO", projects: ["O"]), Type("any")], Now, maxWorkers: 2);
        Assert.Equal(["P:forP", "O:forO"], starts.Select(x => $"{x.Project}:{x.Type}"));

        // No type names a project: it serves every project.
        Assert.Equal(2, StartAll([p, o], [Type("any")], Now, maxWorkers: 2).Count);
    }

    // WT-C9: a type's agent is looked up in the project whose card it runs; a project that does not
    // define it gets no card on that type (the other types, and the other projects, are not affected).
    [Fact]
    public void PoolTypes_ProjectWithoutTheAgent_TypeDoesNotFit()
    {
        var p = new PoolProject("P", 70, Project("P", 70, Card(CardFolder.Queue, "010-A-p")).Plan) { Agents = new HashSet<string>(Text.Comparer) { "copilot" } };
        var o = new PoolProject("O", 30, Project("O", 30, Card(CardFolder.Queue, "010-A-o")).Plan) { Agents = new HashSet<string>(Text.Comparer) { "claude", "copilot" } };

        // The claude type fits O only; P has no claude agent.
        var starts = StartAll([p, o], [Type("c", agent: "Claude")], Now, maxWorkers: 2);
        Assert.Equal(["O"], starts.Select(x => x.Project));

        // With a second type P can use, P's card goes there.
        starts = StartAll([p, o], [Type("c", agent: "claude"), Type("cp", agent: "copilot")], Now, maxWorkers: 2);
        Assert.Equal(["O:c", "P:cp"], starts.Select(x => $"{x.Project}:{x.Type}").Order());

        // The default agent ("") is always defined; a project with no restriction defines every agent.
        Assert.Single(StartAll([p], [Type("d")], Now));
        Assert.Single(StartAll([Project("P", 70, Card(CardFolder.Queue, "010-A-p"))], [Type("c", agent: "claude")], Now));
        Assert.Empty(StartAll([p], [Type("c", agent: "claude")], Now));
    }

    // WT-C3, WT-C8 (what PoolConfig reads): each type of the top-level pool section names the projects it
    // serves, in the file's order; a type naming none serves all.
    [Fact]
    public void PoolTypes_PoolSection_ProjectsPerTypeParsed()
    {
        const string json = """
            { "pool": { "maxWorkers": 3, "types": {
                "spark":  { "max": 2, "areas": "abc", "projects": ["AmigaX", "Other"] },
                "sonnet": { "max": 1, "agent": "claude", "premium": true }
            } } }
            """;
        var pool = Scheduler.Core.Config.WorkerTypesJson.Parse(json, new SchedulerConfig());
        Assert.NotNull(pool);
        Assert.Equal(3, pool.MaxWorkers);
        Assert.Equal(["spark", "sonnet"], pool.Types.Select(t => t.Name));
        Assert.Equal(["AmigaX", "Other"], pool.Types[0].Projects);
        Assert.Empty(pool.Types[1].Projects);
    }

    // WT-6 (pool form): a start that fails is not tried again this tick, and the next card goes.
    [Fact]
    public void PoolTypes_FailedStart_NotTriedAgainThisTick()
    {
        var p = Project("P", 70, Card(CardFolder.Queue, "010-A-p1"), Card(CardFolder.Queue, "020-B-p2"));
        var tried = new List<string>();
        var starts = StartAll([p], [Type("w1")], Now, maxWorkers: 2, start: o =>
        {
            tried.Add(o.Candidate.Card.Id);
            return o.Candidate.Card.Id != "010-A";
        });

        Assert.Equal(["010-A", "020-B"], tried);
        Assert.Equal(["020-B"], starts.Select(x => x.Candidate.Card.Id));
    }

    private sealed class At(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }
}
