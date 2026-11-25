// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Health;
using Scheduler.Core.Planner;
using Scheduler.Core.Pool;
using Scheduler.Core.Tests.Health;
using static Scheduler.Core.Tests.Pool.PoolFakes;

namespace Scheduler.Core.Tests.Pool;

/// <summary>
/// What stays per project in the shared pool (spec B1 MP-9) and the answers to spec B3 Q11, Q13
/// and Q14 as given on card 310-A: a red main stops only that project; a quota pause stops only
/// that agent's workers, in every project; the <c>Worker:</c> request works as today; retries and
/// time limits come from each project's own settings. Test plan:
/// scheduler-next/test-plans/weighted-effort.md. Projects P (weight 70) and O (weight 30).
/// </summary>
public class PoolProjectRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static Card Card(CardFolder folder, string name, string? worker = null, int? timeBox = null)
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
            Status = "ready",
            Prio = 1,
            WorkerReq = worker,
            TimeBox = timeBox,
        };
    }

    private static PoolProject Project(string name, int weight, params Card[] cards) =>
        new(name, weight, DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2));

    // MP-9: the planner outbox and the health command are each project's own. They run against that
    // project's repository only, and a project without them shows no planner line and no health state.
    [Fact]
    public void Pool_PlannerHealthGates_PerProject()
    {
        var p = new PoolProjectSetup(
            "P",
            @"C:\repos\P",
            new SchedulerConfig { HealthCommand = "dotnet build P.slnx" },
            PlannerSettings.Create(@"C:\fetch\P", "outbox"));
        var o = new PoolProjectSetup("O", @"C:\repos\O", new SchedulerConfig(), null);

        var health = PoolProjectSetup.HealthRuns([p, o]);
        var only = Assert.Single(health);
        Assert.Equal(("P", @"C:\repos\P", "dotnet build P.slnx"), (only.Project, only.Repo, only.Command));

        var planner = PoolProjectSetup.PlannerOutboxes([p, o]);
        var outbox = Assert.Single(planner);
        Assert.Equal("P", outbox.Project);
        Assert.Equal(Path.Combine(@"C:\fetch\P", "outbox"), outbox.Outbox);

        Assert.True(p.ShowsHealthState);
        Assert.True(p.ShowsPlannerLine);
        Assert.False(o.ShowsHealthState);
        Assert.False(o.ShowsPlannerLine);
    }

    private static (HealthCheck Health, FakeHealthRunner Runner) Check(string command)
    {
        var runner = new FakeHealthRunner("abc1234");
        var health = new HealthCheck(new SchedulerConfig { HealthCommand = command }, new FakeClock(), new FakeLog(), runner);
        health.RunFinished();
        Assert.True(health.StartIfWanted());
        return (health, runner);
    }

    // MP-9, HLTH-5, spec B3 Q11 (WT-7): P main goes red, O main is green. P state is red and only P
    // stops; O cards still start. P runs again when its main is green (HLTH-5).
    [Fact]
    public void PoolTypes_RedProjectOffersNothing_OthersRunOn()
    {
        var (pHealth, pRunner) = Check("build P");
        var (oHealth, oRunner) = Check("build O");
        pRunner.FinishWith(1);
        oRunner.FinishWith(0);

        var states = new Dictionary<string, SchedulerState>(Text.Comparer)
        {
            ["P"] = pHealth.Reap(SchedulerState.Running),
            ["O"] = oHealth.Reap(SchedulerState.Running),
        };
        Assert.Equal(SchedulerState.PausedRed, states["P"]);
        Assert.Equal(SchedulerState.Running, states["O"]);

        var p = Project("P", 70, Card(CardFolder.Queue, "010-A-p"));
        var o = Project("O", 30, Card(CardFolder.Queue, "020-A-o"));
        DispatchType[] types = [Type("w1")];

        // P is furthest below its share, and still it does not take the place: it is red.
        PoolRun[] runs = [new("O", T0.AddMinutes(-60), T0)];
        Assert.Equal("O", PoolFakes.StartAll([p, o], types, T0, runs: runs, states: states).Single().Project);

        // Room for two: O starts, P (red) never does.
        Assert.Equal(["O"], PoolFakes.StartAll([p, o], types, T0, maxWorkers: 2, runs: runs, states: states).Select(x => x.Project));

        // O has nothing to run, so the place stays free rather than starting a card of the red project.
        Assert.Empty(PoolFakes.StartAll([p, Project("O", 30)], types, T0, runs: runs, states: states));

        // Green again: P takes its share.
        states["P"] = SchedulerState.Running;
        Assert.Equal("P", PoolFakes.StartAll([p, o], types, T0, runs: runs, states: states).Single().Project);
    }

    // TIME-15, WT-9, spec B3 Q11: a run on agent claude hits quota. Only the types of agent claude
    // stop, in every project: a copilot type still takes cards of P and of O.
    [Fact]
    public void PoolTypes_QuotaPause_PerAgent()
    {
        var p = Project("P", 70, Card(CardFolder.Queue, "010-A-p"));
        var o = Project("O", 30, Card(CardFolder.Queue, "020-A-oc", worker: "cop"), Card(CardFolder.Queue, "030-B-ol", worker: "cla"));
        var claude = Type("cla", agent: "claude");
        var copilot = Type("cop", agent: "copilot");
        var quota = new AgentQuota();
        quota.Mark("Claude", T0);   // names match without case (G-1)
        Assert.True(PoolGate.AgentPaused(claude, quota));
        Assert.False(PoolGate.AgentPaused(copilot, quota));

        // The claude type starts nothing, whichever project has a card for it.
        Assert.Empty(PoolFakes.StartAll([p, o], [claude], T0, maxWorkers: 5, quota: quota));

        // The copilot type takes the O card for it; the P card has no Worker: line, so the copilot
        // type takes it too (listed second: the claude type is out of quota and is passed over).
        var starts = PoolFakes.StartAll([p, o], [claude, copilot], T0, maxWorkers: 5, quota: quota);
        Assert.Equal(["O:020-A:cop", "P:010-A:cop"], starts.Select(x => $"{x.Project}:{x.Candidate.Card.Id}:{x.Type}").Order());

        // The O claude card waits: no other type may take it.
        Assert.DoesNotContain(starts, x => x.Candidate.Card.Id == "030-B");

        // The pause lifted, the claude type takes it again.
        Assert.Contains(PoolFakes.StartAll([o], [claude], T0, maxWorkers: 5), x => x.Candidate.Card.Id == "030-B");
    }

    // DISP-18, WT-3, spec B3 Q13: the Worker: request is kept in the shared pool: no line means only the
    // default type (1146-A); premium is just a name; otherwise the type's slot number (while migrated),
    // name, model or the model's short name, in any case.
    [Fact]
    public void PoolTypes_WorkerRequest_Kept()
    {
        static string? Pick(PoolProject project, DispatchType type, bool migrated = false) =>
            PoolFakes.StartAll([project], [type], T0, migrated: migrated).FirstOrDefault()?.Candidate.Card.Id;

        var plain = Type("w1", model: "big-model", slots: [1]);
        var other = Type("w2", isDefault: false);

        // No Worker: line - only the default type.
        var open = Project("P", 70, Card(CardFolder.Queue, "010-A-open"));
        Assert.Equal("010-A", Pick(open, plain));
        Assert.Null(Pick(open, other));

        // Worker: premium - a name like any other: no type here has it (1146-A).
        var wantsPremium = Project("P", 70, Card(CardFolder.Queue, "020-A-prem", worker: "premium"));
        Assert.Null(Pick(wantsPremium, plain));
        Assert.Null(Pick(wantsPremium, other));

        // Worker: a name, slot number (migrated) or model - that type only, in any case.
        foreach (var request in new[] { "W1", "1", "BIG-MODEL" })
        {
            var named = Project("P", 70, Card(CardFolder.Queue, "030-A-named", worker: request));
            Assert.Equal("030-A", Pick(named, plain, migrated: true));
            Assert.Null(Pick(named, Type("other", slots: [2]), migrated: true));
        }

        // A slot number names nothing once the pool form is saved (530-D Q3).
        Assert.Null(Pick(Project("P", 70, Card(CardFolder.Queue, "035-A-slot", worker: "1")), plain));

        // Taken by a pool type, the request holds in every project it serves.
        var o = Project("O", 30, Card(CardFolder.Queue, "040-A-o", worker: "w1"));
        Assert.Equal("040-A", Pick(o, plain));
        Assert.Null(Pick(o, Type("w3")));
    }

    // DISP-7, TIME-1, spec B3 Q14: maxRetries and the time limit come from each project's own
    // settings. P: 2 retries, 240 minutes. O: 5 retries, 60 minutes.
    [Fact]
    public void Pool_MaxRetriesAndLimits_Source()
    {
        var p = new PoolProjectSetup("P", @"C:\repos\P", new SchedulerConfig { MaxRetries = 2, MaxMinutes = 240 }, null);
        var o = new PoolProjectSetup("O", @"C:\repos\O", new SchedulerConfig { MaxRetries = 5, MaxMinutes = 60 }, null);

        // The same orphan card with 3 runs ended without a result: stuck in P (3 >= 2), resumed in O (3 < 5).
        var retries = new Dictionary<string, int> { ["050-A"] = 3 };
        var cards = new[] { Card(CardFolder.Active, "050-A-orphan") };
        var running = Array.Empty<RunningWorker>();
        CategoriesFile? categories = null;

        var pPlan = p.Plan(cards, running, retries, categories);
        Assert.Equal("050-A", Assert.Single(pPlan.Stuck).Id);
        Assert.Empty(pPlan.Ordered);

        var oPlan = o.Plan(cards, running, retries, categories);
        Assert.Empty(oPlan.Stuck);
        Assert.Equal("050-A", Assert.Single(oPlan.Ordered).Card.Id);

        // The limit is the card's Time box, else the project's own maxMinutes.
        var plain = Card(CardFolder.Queue, "060-A-plain");
        var boxed = Card(CardFolder.Queue, "070-A-boxed", timeBox: 30);
        Assert.Equal(240, p.Limit(plain));
        Assert.Equal(60, o.Limit(plain));
        Assert.Equal(30, p.Limit(boxed));
        Assert.Equal(30, o.Limit(boxed));
    }
}
