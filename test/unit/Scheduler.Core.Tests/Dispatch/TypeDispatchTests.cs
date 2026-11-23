using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Tests.Dispatch;

/// <summary>
/// Dispatch by worker type (worker-types.md section 2, WT-1 to WT-14). Test plan: the table in
/// section 5 of that design. Shared setup: three types in file order - worker (cap 2, areas ABC),
/// Sonnet (premium, cap 1) and Opus (premium, cap 1) - maxWorkers 4, servers up, state running.
/// </summary>
public class TypeDispatchTests
{
    private const string Jobs = "docs/jobs";
    private static readonly DateTimeOffset Noon = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = Noon;
    }

    private sealed class FakeLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    private sealed class FakeProbe : IServerProbe
    {
        public HashSet<string> Down { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsUp(string endpoint) => !Down.Contains(endpoint);

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }

    private static Card Card(string folderAndName, string text = "Status: ready") =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folderAndName}.md", text)
        ?? throw new InvalidOperationException($"not a card path: {folderAndName}");

    private static RunningWorker Running(int slot, string name, string type = "")
    {
        var parts = name.Split('-');
        return new RunningWorker
        {
            Slot = slot, Pid = 100 + slot, Start = DateTimeOffset.UnixEpoch,
            Card = name, Category = parts[1], Id = $"{parts[0]}-{parts[1]}", Type = type,
        };
    }

    // `cards` is what the type's running workers hold (WT-12, 935-A), in slot order, as the caller gives it.
    private static DispatchType Worker(int max = 2, int running = 0, string endpoint = "", string[]? cards = null) =>
        new() { Name = "worker", Max = max, Running = running, Endpoint = endpoint, Slots = [1, 2], RunningCards = cards ?? [] };

    private static DispatchType Sonnet(int max = 1, int running = 0, string[]? cards = null) => new()
    {
        Name = "Sonnet", Max = max, Running = running, Premium = true, Agent = "claude",
        Model = "claude-sonnet-5-5", ModelNames = ["Sonnet-5-5", "sonnet"], Slots = [3], RunningCards = cards ?? [],
    };

    private static DispatchType Opus(int max = 1, int running = 0, string[]? cards = null) => new()
    {
        Name = "Opus", Max = max, Running = running, Premium = true, Agent = "claude",
        Model = "claude-opus-5-5", ModelNames = ["Opus-5-5", "opus"], Slots = [4], RunningCards = cards ?? [],
    };

    private static List<DispatchType> Types() => [Worker(), Sonnet(), Opus()];

    private static DispatchPlan Plan(IReadOnlyList<Card> cards, CategoriesFile? categories = null, params RunningWorker[] running) =>
        DispatchPlanner.Plan(cards, running, new Dictionary<string, int>(), categories, 2);

    private static TypeDispatchResult Dispatch(
        DispatchPlan plan,
        IReadOnlyList<DispatchType> types,
        int maxWorkers = 4,
        int? totalRunning = null,
        SchedulerState state = SchedulerState.Running,
        bool migrated = false,
        AgentQuota? quota = null,
        FakeProbe? probe = null,
        Func<TypeStart, bool>? start = null) =>
        TypeDispatcher.Dispatch(
            plan, state, maxWorkers, totalRunning ?? types.Sum(t => t.Running), types, migrated,
            quota ?? new AgentQuota(),
            new ServerCache(probe ?? new FakeProbe(), new FakeClock(), new FakeLog()),
            new Dictionary<string, int>(), start ?? (_ => true));

    private static string? NoteFor(TypeDispatchResult result, string card) =>
        result.Notes.FirstOrDefault(n => n.Card == card)?.Note;

    private static string[] StartedOn(TypeDispatchResult result) => [.. result.Starts.Select(s => $"{s.Candidate.Card.Id}->{s.Type}")];

    [Fact]
    public void TypeDispatch_TotalReached_NothingStarts_NoteSaysNofM()
    {
        // WT-1, WT-12 step 4: 2 of 2 run in all, though the type's own cap is 3.
        var types = new List<DispatchType> { Worker(max: 3, running: 2) };
        var plan = Plan([Card("queue/010-A-x")], null, Running(1, "020-B-a", "worker"), Running(2, "030-C-b", "worker"));

        var result = Dispatch(plan, types, maxWorkers: 2);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for a free worker (2 of 2 running)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_TypeAtCap_NextCardOfOtherTypeStarts()
    {
        // WT-2, WT-6: Sonnet is full, so the premium card waits and the next card (non-premium) goes on.
        var types = new List<DispatchType> { Worker(), Sonnet(running: 1) };
        var plan = Plan([Card("queue/010-A-x", "Status: ready\nWorker: premium"), Card("queue/020-B-y")]);

        var result = Dispatch(plan, types);

        Assert.Equal(["020-B->worker"], StartedOn(result));
        Assert.Equal("next up; waiting for Sonnet (1 of 1 running)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_CardAsksNamedWorker_FullTypeNamesItsCard()
    {
        // WT-12, DISP-19 in type form (760-A Q3, 935-A): the Queue says which card the busy type holds.
        var plan = Plan([Card("queue/810-D-x", "Status: ready\nWorker: Opus")]);

        var result = Dispatch(plan, [Worker(), Opus(running: 1, cards: ["530-D"])]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for Opus (1 of 1 running: 530-D)", NoteFor(result, "810-D-x"));
    }

    [Fact]
    public void TypeDispatch_TypeFull_NoteNamesItsCardsInSlotOrder()
    {
        // WT-12, DISP-19 (935-A): the caller gives the type's cards in slot order and the note keeps it.
        var plan = Plan([Card("queue/010-A-x")], null, Running(2, "020-B-a", "worker"), Running(1, "030-C-b", "worker"));

        var result = Dispatch(plan, [Worker(max: 2, running: 2, cards: ["020-B", "030-C"])]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for worker (2 of 2 running: 020-B, 030-C)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_TypeFull_MoreThanThreeNamed_ThreeThenPlusCount()
    {
        // WT-12 (935-A): three cards are named, the rest counted.
        var plan = Plan([Card("queue/010-A-x")]);

        var result = Dispatch(plan, [Worker(max: 4, running: 4, cards: ["010-A", "020-B", "030-C", "040-D", "050-E"])]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for worker (4 of 4 running: 010-A, 020-B, 030-C+2)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_TypeFull_CallerSaysNoCards_NoteSaysTheCountOnly()
    {
        // 800-B's rule in type form: the reason is still said when the caller does not name the cards.
        var plan = Plan([Card("queue/010-A-x")]);

        var result = Dispatch(plan, [Worker(max: 2, running: 2)]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for worker (2 of 2 running)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_TypeHasRoom_NoteSaysNothingExtra()
    {
        // WT-12: a type with room is no reason to wait, so the cards it holds are not named.
        var plan = Plan([Card("queue/010-A-x")], null, Running(1, "020-B-a", "worker"));

        var result = Dispatch(plan, [Worker(max: 2, running: 1, cards: ["020-B"])], maxWorkers: 1, totalRunning: 1);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for a free worker (1 of 1 running)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_CapZero_TypeOff_NoteSaysOff()
    {
        // WT-2, WT-12 step 3: max 0 is what Disable was.
        var plan = Plan([Card("queue/010-A-x", "Status: ready\nWorker: premium")]);

        var result = Dispatch(plan, [Worker(), Sonnet(max: 0)]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for Sonnet is off", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_CapLoweredBelowRunning_NothingStopped_NoneStarted()
    {
        // WT-2, WT-13: cap 1 while 2 run: no new one starts (and nothing here stops a running one).
        var plan = Plan([Card("queue/010-A-x")], null, Running(1, "020-B-a", "worker"), Running(2, "030-C-b", "worker"));

        var result = Dispatch(plan, [Worker(max: 1, running: 2)]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for worker (2 of 1 running)", NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_NoWorkerLine_OnlyNonPremium()
    {
        // WT-3: Sonnet is listed first and has room, but a card without a Worker: line goes to a non-premium type.
        var plan = Plan([Card("queue/010-A-x")]);

        var result = Dispatch(plan, [Sonnet(), Worker()]);

        Assert.Equal(["010-A->worker"], StartedOn(result));
    }

    [Fact]
    public void TypeDispatch_WorkerPremium_OnlyPremium_FirstListed()
    {
        // WT-3, WT-5: of the premium types, the one listed first.
        var plan = Plan([Card("queue/010-A-x", "Status: ready\nWorker: Premium")]);

        var result = Dispatch(plan, [Worker(), Opus(), Sonnet()]);

        Assert.Equal(["010-A->Opus"], StartedOn(result));
    }

    [Theory]
    [InlineData("Worker: sonnet", "Sonnet")]
    [InlineData("Worker: SONNET", "Sonnet")]
    [InlineData("Worker: claude-opus-5-5", "Opus")]
    [InlineData("Worker: Opus-5-5", "Opus")]
    [InlineData("Worker: opus", "Opus")]
    [InlineData("Worker: worker", "worker")]
    [InlineData("Worker: gpt", "")]
    public void TypeDispatch_WorkerName_ModelOrShort_PicksThatType(string line, string expected)
    {
        // WT-3: the type's name, its model, Model-Short of it or models.json's short.
        var plan = Plan([Card("queue/010-A-x", $"Status: ready\n{line}")]);

        var result = Dispatch(plan, Types());

        Assert.Equal(expected.Length == 0 ? [] : [$"010-A->{expected}"], StartedOn(result));
    }

    [Fact]
    public void TypeDispatch_WorkerSlotNumber_MatchesMigratedTypeOnly()
    {
        // WT-3, 530-D Q3: a slot number names the type migrated from it, until the pool form is saved.
        var plan = Plan([Card("queue/010-A-x", "Status: ready\nWorker: 4")]);

        var migrated = Dispatch(plan, Types(), migrated: true);
        var saved = Dispatch(plan, Types(), migrated: false);

        Assert.Equal(["010-A->Opus"], StartedOn(migrated));
        Assert.Empty(saved.Starts);
        Assert.Equal("next up; asks for worker 4, and no worker type fits", NoteFor(saved, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_AnyCategory_FitsATypeWithoutAWorkerLine()
    {
        // WT-3, 969-A: worker types have no area list, so a design card (D) with no Worker: line goes to worker.
        var plan = Plan([Card("queue/010-D-x")]);

        var result = Dispatch(plan, Types());

        Assert.Equal(["010-D->worker"], StartedOn(result));
    }

    [Fact]
    public void TypeDispatch_ServerDown_TypeNotUsable_OtherTypeTakesCard()
    {
        // WT-4, WT-10: the first type's server does not answer, so the second takes the card.
        var probe = new FakeProbe();
        probe.Down.Add("http://spark:1/v1");
        var spark = Worker(endpoint: "http://spark:1/v1") with { Name = "spark" };
        var local = new DispatchType { Name = "local", Max = 1 };

        var result = Dispatch(Plan([Card("queue/010-A-x")]), [spark, local], probe: probe);
        Assert.Equal(["010-A->local"], StartedOn(result));

        var alone = Dispatch(Plan([Card("queue/010-A-x")]), [spark], probe: probe);
        Assert.Equal("next up; waiting for spark: model server does not answer", NoteFor(alone, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_SeveralTypesUsable_FirstListedTakesIt()
    {
        // WT-5, WT-C5: the cheaper type is listed first.
        var types = new List<DispatchType> { new() { Name = "cheap", Max = 1 }, new() { Name = "dear", Max = 1 } };

        var result = Dispatch(Plan([Card("queue/010-A-x"), Card("queue/020-B-y")]), types);

        Assert.Equal(["010-A->cheap", "020-B->dear"], StartedOn(result));
    }

    [Fact]
    public void TypeDispatch_WaitingCardKeepsScope_OverlappingLaterCardWaits()
    {
        // WT-8, DISP-13: 010-A waits for Opus (full); 020-A overlaps its scope and does not overtake it.
        var categories = new CategoriesFile
        {
            Categories = new Dictionary<string, CategoryDefinition>(Text.Comparer)
            {
                ["A"] = new() { Letter = "A", Name = "core", Owns = ["src/core/"] },
            },
            AddOnly = [],
        };
        var plan = Plan(
            [Card("queue/010-A-one", "Status: ready\nWorker: opus"), Card("queue/020-A-two")], categories);

        var result = Dispatch(plan, [Worker(), Opus(running: 1)]);

        Assert.Empty(result.Starts);
        Assert.Equal("next up; waiting for Opus (1 of 1 running)", NoteFor(result, "010-A-one"));
        Assert.Equal("waits: overlaps 010-A on src/core/", NoteFor(result, "020-A-two"));
    }

    [Fact]
    public void TypeDispatch_QuotaOnClaude_CopilotTypesRunOn_StateStaysRunning()
    {
        // WT-9: claude out of quota - Claude types wait, the default agent's type runs on.
        var quota = new AgentQuota();
        quota.Mark("claude", Noon.AddMinutes(-35));   // 11:25
        var plan = Plan([Card("queue/010-A-x", "Status: ready\nWorker: premium"), Card("queue/020-B-y")]);

        var result = Dispatch(plan, Types(), quota: quota);

        Assert.Equal(["020-B->worker"], StartedOn(result));
        Assert.Equal("next up; waiting for Sonnet: claude out of quota since 11:25, Opus: claude out of quota since 11:25",
            NoteFor(result, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_AgentResume_ClearsOnlyThatAgent()
    {
        // WT-9: the panel's Resume for one agent; the page's Resume clears all.
        var quota = new AgentQuota();
        quota.Mark("claude", Noon);
        quota.Mark("Copilot", Noon);

        Assert.True(quota.Clear("CLAUDE"));
        Assert.False(quota.IsOut("claude"));
        Assert.True(quota.IsOut("copilot"));

        quota.ClearAll();
        Assert.Empty(quota.Agents);
    }

    [Theory]
    [InlineData(8, 4, 2)]
    [InlineData(16, 4, 4)]
    [InlineData(3, 4, 1)]
    public void TypeDispatch_Threads_CoresOverMaxWorkers(int cores, int maxWorkers, int expected)
    {
        // WT-11: threads per worker = max(1, floor(cores / maxWorkers)).
        Assert.Equal(expected, WorkerSelection.JobsPerWorker(cores, maxWorkers));
    }

    [Fact]
    public void TypeDispatch_Notes_EachReasonInOrder()
    {
        // WT-12: state first, then no type fits, then each type's reason, then the total.
        var card = Card("queue/010-A-x");
        var orphan = Card("active/020-B-y");
        var plan = Plan([card, orphan]);

        var paused = Dispatch(plan, Types(), state: SchedulerState.PausedRed);
        Assert.Equal("next up; dispatching is paused-red", NoteFor(paused, "010-A-x"));
        Assert.Equal("orphan to resume; next up; dispatching is paused-red", NoteFor(paused, "020-B-y"));

        var probe = new FakeProbe();
        probe.Down.Add("http://down:1");
        var several = new List<DispatchType>
        {
            Worker(max: 0), Worker(running: 2) with { Name = "full" }, Worker() with { Name = "quota", Agent = "x" },
            Worker(endpoint: "http://down:1") with { Name = "down" },
        };
        var quota = new AgentQuota();
        quota.Mark("x", Noon);
        var why = Dispatch(Plan([card]), several, quota: quota, probe: probe);
        Assert.Equal(
            "next up; waiting for worker is off, full (2 of 2 running), quota: x out of quota since 12:00, down: model server does not answer",
            NoteFor(why, "010-A-x"));
    }

    [Fact]
    public void TypeDispatch_Fitting_IgnoresRunningAndQuota()
    {
        // WT-12 (the Queue window's worker column): the type that would take the card if nothing ran.
        var types = new List<DispatchType> { Worker(running: 2), Sonnet(max: 0), Opus(running: 1) };

        Assert.Equal("worker", TypeDispatcher.Fitting(types, Card("queue/010-A-x"), migrated: false)?.Name);
        Assert.Equal("Opus", TypeDispatcher.Fitting(types, Card("queue/010-A-x", "Status: ready\nWorker: premium"), migrated: false)?.Name);
        Assert.Equal("worker", TypeDispatcher.Fitting(types, Card("queue/010-D-x"), migrated: false)?.Name); // 969-A: any category
        Assert.Null(TypeDispatcher.Fitting(types, Card("queue/010-A-x", "Status: ready\nWorker: nobody"), migrated: false));
    }

    [Fact]
    public void TypeDispatch_DryRun_LogsTypeAndSlot()
    {
        // WT-14, DISP-21.
        Assert.Equal("DRY RUN Opus in slot 3 <- 010-A-x", TypeDispatcher.DryRunLine("Opus", 3, Card("queue/010-A-x"), resume: false));
        Assert.Equal("DRY RUN Opus in slot 3 <- 010-A-x (resume)", TypeDispatcher.DryRunLine("Opus", 3, Card("queue/010-A-x"), resume: true));
    }

    [Fact]
    public void TypeDispatch_StartWorkerFails_CardNotTriedAgainButTheNextIs()
    {
        // WT-6 (the rule the slot dispatcher's DISP-15 test carried): a start that fails is not tried
        // again this tick, but the type's place stays free for the next candidate.
        var plan = Plan([Card("queue/010-A-x"), Card("queue/020-B-y")]);

        var result = Dispatch(plan, [Worker(max: 1)], start: s => s.Candidate.Card.Id != "010-A");

        Assert.Equal(
            [("worker", "010-A", false), ("worker", "020-B", true)],
            result.Starts.Select(s => (s.Type, s.Candidate.Card.Id, s.Started)));
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void TypeDispatch_ResumeCountsRetryBeforeStart()
    {
        // DISP-16: an orphan's resume counts before its start; a new card's start does not.
        var plan = Plan([Card("active/010-A-x"), Card("queue/020-B-y")]);
        var retries = new Dictionary<string, int>();

        var result = TypeDispatcher.Dispatch(
            plan, SchedulerState.Running, 4, 0, Types(), false, new AgentQuota(),
            new ServerCache(new FakeProbe(), new FakeClock(), new FakeLog()), retries, _ => false);

        Assert.Equal(2, result.Starts.Count);
        Assert.True(result.Starts[0].Candidate.Resume);
        Assert.Equal(1, retries["010-A"]);
        Assert.False(retries.ContainsKey("020-B"));
    }
}
