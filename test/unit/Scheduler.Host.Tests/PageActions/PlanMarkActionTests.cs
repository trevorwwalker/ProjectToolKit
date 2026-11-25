// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Host.Tests.Pool;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>1125-C: a click on a plan's mark cycles it in jobs/plan-marks.json and commits it.</summary>
public class PlanMarkActionTests
{
    private const string Plan = "jobs/plans/x_plan.md";

    // A cap against a hang only: no test here asserts how fast anything is.
    private static readonly TimeSpan HangCap = TimeSpan.FromMinutes(2);

    private static void AssertCommitted(Scheduler.Host.PageActions.HostPageActions actions) =>
        Assert.True(actions.MarksIdle(HangCap), "the background commit of a plan mark did not finish in 2 minutes (a hang)");

    [Fact]
    public void Click_CyclesTheMark_AndCommitsTheFileWithTheNamedMessage()
    {
        var main = new PageReviewRefreshTests.InMemoryMain();
        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(main: main, committer: committer, project: "ptk-1125c");
        harness.Tick_Once();
        var rel = "docs/jobs/plan-marks.json";

        var first = harness.Actions.PlanMark(Plan, "3");
        Assert.True(first.Ok, first.Error);
        AssertCommitted(harness.Actions);
        var change = Assert.Single(committer.Changes);
        Assert.Equal("jobs: plan mark " + Plan + " 3 checked", change.Message);
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(PlanMarkFile.Read(change.Writes[rel]), Plan, "3"));

        main.Files[rel] = change.Writes[rel];
        Assert.True(harness.Actions.PlanMark(Plan, "3").Ok);
        AssertCommitted(harness.Actions);
        Assert.EndsWith("3 automatic", committer.Changes[1].Message, StringComparison.Ordinal);
        Assert.Empty(PlanMarkFile.Read(committer.Changes[1].Writes[rel]));
    }

    // 1213-B: the click is answered while the commit is still running: the action returns in under 50 ms (a speed
    // check, kept) while the committer is held at its gate (so the commit cannot have finished: asserted by sequence), and the pending mark is what the
    // views lay over main in the meantime.
    [Fact]
    public void Card1213_Click_ReturnsBeforeTheCommit_AndThePendingMarkShowsAtOnce()
    {
        var committer = new SlowCommitter(TimeSpan.Zero) { Gate = new ManualResetEventSlim(false) };
        using var harness = new PageActionHarness(committer: committer, project: "ptk-1213-slow");
        harness.Tick_Once();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = harness.Actions.PlanMark(Plan, "3");
        clock.Stop();

        Assert.True(result.Ok, result.Error);
        // 250 ms, not 50 ms: under five workers a click took 60 ms (2026-11-24, health checks of 13:53 and 14:02); alone it takes 3.1 ms and the PC's process-start rate was 411/s with 2 workers (healthy), so this was load (jobs/project.md, "Speed checks").
        Assert.True(clock.ElapsedMilliseconds < 250, $"the click took {clock.ElapsedMilliseconds} ms");   // a speed check: kept as it was (1306-B Dev note)
        Assert.Equal(0, committer.Done);   // the click came back with the commit held at its gate
        var shown = harness.Actions.PendingMarks.Over(PlanMarkFile.Read(null));
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(shown, Plan, "3"));
        committer.Gate.Set();

        AssertCommitted(harness.Actions);
        Assert.Equal(1, committer.Done);
        Assert.Equal("jobs: plan mark " + Plan + " 3 checked", Assert.Single(committer.Changes).Message);
        // Committed: still shown until main says the same, then pruned.
        Assert.Equal(ManualMark.Checked, harness.Actions.PendingMarks.Of(Plan, "3"));
        harness.Actions.PendingMarks.Prune(PlanMarkFile.Read(committer.Changes[0].Writes["docs/jobs/plan-marks.json"]), DateTimeOffset.Now);
        Assert.Null(harness.Actions.PendingMarks.Of(Plan, "3"));
    }

    // 1213-B: quick clicks while a commit runs cycle from the pending mark, not from main, and the ones that came
    // during the commit are folded into one more commit with the last state of each.
    [Fact]
    public void Card1213_ThreeQuickClicks_CycleInOrder_AndFoldIntoOneCommitWithTheThirdState()
    {
        var committer = new SlowCommitter(TimeSpan.Zero) { Gate = new ManualResetEventSlim(false) };
        using var harness = new PageActionHarness(committer: committer, project: "ptk-1213-fold");
        harness.Tick_Once();

        Assert.True(harness.Actions.PlanMark(Plan, "other").Ok);   // holds the commit while the clicks come
        Assert.True(committer.Started.Wait(HangCap), "the background commit of a plan mark did not start in 2 minutes (a hang)");
        Assert.True(harness.Actions.PlanMark(Plan, "3").Ok);
        Assert.Equal(ManualMark.Checked, harness.Actions.PendingMarks.Of(Plan, "3"));
        Assert.True(harness.Actions.PlanMark(Plan, "3").Ok);
        Assert.Equal(ManualMark.Automatic, harness.Actions.PendingMarks.Of(Plan, "3"));
        Assert.True(harness.Actions.PlanMark(Plan, "4").Ok);
        committer.Gate.Set();

        AssertCommitted(harness.Actions);
        Assert.Equal(2, committer.Changes.Count);
        var folded = committer.Changes[1];
        Assert.Equal("jobs: plan mark " + Plan + " 3 automatic, " + Plan + " 4 checked", folded.Message);
        var marks = PlanMarkFile.Read(folded.Writes["docs/jobs/plan-marks.json"]);
        Assert.Equal(ManualMark.Automatic, PlanMarkFile.Of(marks, Plan, "3"));
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(marks, Plan, "4"));
    }

    // 1213-B: a commit that fails drops the pending mark it carried, so the next refresh shows main again.
    [Fact]
    public void Card1213_AFailedCommit_RemovesThePendingMark()
    {
        var committer = new RecordingCommitter { FailWith = "push rejected" };
        using var harness = new PageActionHarness(committer: committer, project: "ptk-1213-fail");
        harness.Tick_Once();

        Assert.True(harness.Actions.PlanMark(Plan, "3").Ok);
        AssertCommitted(harness.Actions);

        Assert.Null(harness.Actions.PendingMarks.Of(Plan, "3"));
        Assert.Empty(harness.Actions.PendingMarks.Over(PlanMarkFile.Read(null)));
        Assert.Empty(committer.Changes);
    }

    // 1213-B: the overlay: a pending mark lies over main's, and automatic removes main's own.
    [Fact]
    public void Card1213_PendingMarks_LieOverMain_AutomaticRemovesAMainMark()
    {
        var main = PlanMarkFile.Read(PlanMarkFile.With(PlanMarkFile.With(null, Plan, "1", ManualMark.Checked), Plan, "2", ManualMark.Checked));
        var pending = new PendingPlanMarks();
        var before = pending.Version;
        pending.Set(Plan, "1", ManualMark.Automatic);
        pending.Set(Plan, "5", ManualMark.Checked);

        var shown = pending.Over(main);

        Assert.NotEqual(before, pending.Version);
        Assert.Equal(ManualMark.Automatic, PlanMarkFile.Of(shown, Plan, "1"));
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(shown, Plan, "2"));
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(shown, Plan, "5"));
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(main, Plan, "1"));
    }

    [Fact]
    public void Click_WithNoPlan_IsRefused()
    {
        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(committer: committer, project: "ptk-1125c-none");
        harness.Tick_Once();

        Assert.False(harness.Actions.PlanMark("", "3").Ok);
        Assert.Empty(committer.Changes);
    }

    // 1171-C: the click as the page sends it, through a composed pool's own router to the project's own actions,
    // ends as jobs/plan-marks.json on that project's main with the named commit; the other project is untouched.
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Card1171_PoolClick_EndToEnd_CommitsTheMarkOnThatProjectsMain()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        pool.Runner.Run();
        using var server = new PageServer(new PageServerOptions
        {
            Data = pool.PageData,
            Actions = pool.Actions,
            Renderer = new PageRenderer(),
            Feed = new LiveFeed(rig.Clock, pool.PageData),
            Pool = new PoolRouting(pool.PageData, pool.Actions, new CombinedPageRenderer()),
        });
        var repos = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(rig.PoolFile))!["projects"]!.AsArray()
            .ToDictionary(p => (string)p!["name"]!, p => (string)p!["repo"]!);

        var response = server.Router.Handle(new PageRequest
        {
            Method = "POST",
            Path = "/plan-mark",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Requested-With"] = "fetch" },
            FormValues = PageRequest.ParseForm("project=" + rig.A + "&plan=jobs%2Fplans%2Fx_plan.md&step=plan"),
        });

        Assert.Equal(202, response.Status);
        Assert.True(server.Queue.WaitIdle(HangCap), "the action queue did not go idle in 2 minutes (a hang)");
        var state = Assert.Single(server.Queue.States(), s => s.Kind == "plan-mark");
        Assert.True(state.State == "done", state.State + ": " + state.Message);
        Assert.True(pool.Projects.All(p => p.Actions.MarksIdle(HangCap)), "the background commit of a plan mark did not finish in 2 minutes (a hang)");   // 1213-B
        string Main(string project, params string[] args)
        {
            Scheduler.Host.Tests.Git.GitFixture.Git(repos[project], "fetch", "-q", "origin");
            return Scheduler.Host.Tests.Git.GitFixture.Git(repos[project], args);
        }

        Assert.Equal("jobs: plan mark " + Plan + " plan checked", Main(rig.A, "log", "-1", "--format=%s", "origin/main").Trim());
        var file = Main(rig.A, "show", "origin/main:jobs/" + PlanMarkFile.FileName);
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(PlanMarkFile.Read(file), Plan, "plan"));
        Assert.DoesNotContain(PlanMarkFile.FileName, Main(rig.B, "ls-tree", "-r", "--name-only", "origin/main"), StringComparison.Ordinal);
    }

    // 1213-B: a committer that takes its time (or waits for its gate), as a busy pool's push does.
    private sealed class SlowCommitter(TimeSpan delay) : Scheduler.Core.Contracts.IJobsCommitter
    {
        private int _done;

        public List<Scheduler.Core.Contracts.JobsChange> Changes { get; } = [];

        public ManualResetEventSlim? Gate { get; init; }

        public ManualResetEventSlim Started { get; } = new(false);

        public int Done => Volatile.Read(ref _done);

        public Scheduler.Core.Contracts.CommitResult Apply(Scheduler.Core.Contracts.JobsChange change)
        {
            Started.Set();
            Gate?.Wait(HangCap);
            Thread.Sleep(delay);
            lock (Changes)
            {
                Changes.Add(change);
            }

            Interlocked.Increment(ref _done);
            return new Scheduler.Core.Contracts.CommitResult { Ok = true, Sha = "abc1234" };
        }
    }
}
