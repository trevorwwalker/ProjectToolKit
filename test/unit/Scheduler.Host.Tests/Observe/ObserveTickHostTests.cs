using Scheduler.Core.Contracts;
using Scheduler.Host.Observe;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Tick;

namespace Scheduler.Host.Tests.Observe;

/// <summary>
/// The observe run's tick host (card 390-B): like dry run (DISP-21) it starts nothing, and it also
/// leaves every write to git and to the old scheduler's workers alone.
/// </summary>
public sealed class ObserveTickHostTests
{
    private sealed class Inner : ITickHost
    {
        public List<string> Calls { get; } = [];

        public RunningWorker? StartWorker(int slot, Card card, bool resume) { Calls.Add("start"); return null; }

        public void IndexLogs() => Calls.Add("index");

        public IReadOnlyList<Card>? ReadCards() { Calls.Add("cards"); return []; }

        public bool FixDuplicates(IReadOnlyList<Card> cards) { Calls.Add("duplicates"); return true; }

        public void PlannerStep() => Calls.Add("planner");

        public string? ReadCategories() { Calls.Add("lanes"); return null; }

        public string? ReadWorkers() { Calls.Add("workers"); return null; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels() { Calls.Add("models"); return new Dictionary<string, IReadOnlyList<string>>(); }

        public IReadOnlyList<JobRequest> ReadRequests() { Calls.Add("requests"); return []; }

        public RunningWorker Watch(RunningWorker worker) { Calls.Add("watch"); return worker with { Abandoned = true }; }

        public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries) { Calls.Add("finish"); return SchedulerState.PausedQuota; }

        public void SaveRetries(IReadOnlyDictionary<string, int> retries) => Calls.Add("retries");

        public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state) => Calls.Add("status");
    }

    private static readonly Card Card = new() { Folder = CardFolder.Queue, Path = "docs/jobs/queue/390-B-x.md", Name = "390-B-x", Num = 390, Category = "B", Id = "390-B" };

    private static readonly RunningWorker Worker = new() { Slot = 2, Pid = 42, Start = DateTimeOffset.Now, Card = "390-B-x", Category = "B", Id = "390-B" };

    [Fact]
    public void A_start_is_logged_with_its_time_and_nothing_starts()
    {
        // DISP-21 (logged instead of started), roadmap item 1
        var inner = new Inner();
        var log = new ListLog();
        var clock = new SkewClock();
        var host = new ObserveTickHost(inner, log, clock);

        Assert.Null(host.StartWorker(3, Card, resume: true));
        Assert.Null(host.StartWorker(1, Card, resume: false));

        Assert.Empty(inner.Calls);
        Assert.Matches(@"^OBSERVE \d{4}-\d\d-\d\d \d\d:\d\d:\d\d slot 3 <- 390-B-x \(resume\)$", log.Lines[0]);
        Assert.EndsWith(" slot 1 <- 390-B-x", log.Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicates_the_planner_the_watchdog_and_finishing_are_left_to_the_old_scheduler()
    {
        // CARD-19 (duplicate fix commits), PLAN-1 (planner commits), TIME-3 and TIME-20 (watchdog and finish), roadmap item 1
        var inner = new Inner();
        var log = new ListLog();
        var host = new ObserveTickHost(inner, log, new SkewClock());

        Assert.False(host.FixDuplicates([Card]));
        host.PlannerStep();
        Assert.Same(Worker, host.Watch(Worker));
        Assert.Equal(SchedulerState.Paused, host.Finish(Worker, SchedulerState.Paused, new Dictionary<string, int>()));

        Assert.Empty(inner.Calls);
        Assert.EndsWith(" slot 2 finished: 390-B-x", Assert.Single(log.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_and_the_own_folder_files_are_the_inner_hosts()
    {
        // DISP-21 (everything else is the inner host's), roadmap item 1
        var inner = new Inner();
        var host = new ObserveTickHost(inner, new ListLog(), new SkewClock());

        host.IndexLogs();
        host.ReadCards();
        host.ReadCategories();
        host.ReadWorkers();
        host.ReadModels();
        host.ReadRequests();
        host.SaveRetries(new Dictionary<string, int>());
        host.WriteStatus([], SchedulerState.Running);

        Assert.Equal(["index", "cards", "lanes", "workers", "models", "requests", "retries", "status"], inner.Calls);
    }
}
