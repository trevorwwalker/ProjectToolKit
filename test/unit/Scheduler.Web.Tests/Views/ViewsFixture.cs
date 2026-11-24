using Scheduler.Core;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// The snapshots the views are tested against. <see cref="Full"/> is PAGE-5's "one of
/// everything"; <see cref="Plain"/> is the smallest page a test can render.
/// </summary>
internal static class ViewsFixture
{
    public static readonly DateTimeOffset Built = new(2026, 11, 18, 12, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// The version the snapshots carry (951-A): the build's own, read from
    /// <see cref="CoreInfo.Version"/>, so no test writes the number and every one of them checks that
    /// what the page shows is what was built (CFG-18).
    /// </summary>
    public static readonly string ShownVersion = CoreInfo.Version;

    /// <summary>A page with nothing in it beyond the top bar and the Control window.</summary>
    public static PageSnapshot Plain(SchedulerState state = SchedulerState.Running) => new()
    {
        Built = Built,
        Project = "ProjectToolkit",
        Version = ShownVersion,
        State = new PageStateView { State = state, Text = StateWord(state) },
    };

    /// <summary>One of everything PAGE-5's windows show (Page_Windows_Present's setup).</summary>
    public static PageSnapshot Full() => new()
    {
        Built = Built,
        Project = "ProjectToolkit",
        Version = ShownVersion,
        Theme = "Modern",
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
        HostLoad = new HostLoadSample
        {
            CpuPercent = 42,
            FreeMemoryBytes = 8L * 1024 * 1024 * 1024,
            TotalMemoryBytes = 32L * 1024 * 1024 * 1024,
            Cores = 8,
        },
        Health = new PageHealthView
        {
            Green = true,
            Text = "main 1a2b3c4 green at 12:20",
            ServerDown = true,
            CategoryOverlaps = ["lane A and lane B both own src/Shared/"],
        },
        // Multi-line, as CardStats.Summary writes it (ACCT-6): the overall line and one per model.
        Stats = new PageStats
        {
            Cards = 4,
            Minutes = 90,
            Text = "Last 24 h (1 h 30 min): 4 cards (5 runs): done 3, 1 in review, 0 waiting on questions, 0 open; 0.17 cards/hr\n"
                 + "  opus (1 h 0 min): 2 cards (3 runs): done 2, 0 in review, 0 waiting on questions, 0 open; 0.08 cards/hr\n"
                 + "  gpt (30 min): 2 cards (2 runs): done 1, 1 in review, 0 waiting on questions, 0 open; 0.08 cards/hr",
        },
        PlannerLine = "planner push: on, 2 waiting",
        PlannerPush = true,
        // The Worker types panel's rows, in the config's order (WT-P3): the default type at its cap,
        // one more type idle (worker is the default, 1146-A), and one turned off (WT-C3's cap 0).
        WorkerTypes =
        [
            new PageWorkerTypeView { Name = "worker", Running = 2, Max = 2, Model = "spark", IsDefault = true },
            new PageWorkerTypeView { Name = "Sonnet", Running = 0, Max = 1, Agent = "claude", Model = "claude-sonnet-5-5", Endpoint = "http://claude.local:8080/v1", MetricsUrl = "http://claude.local:9090/metrics" },
            new PageWorkerTypeView { Name = "Opus", Running = 0, Max = 0, Agent = "claude", Model = "claude-opus-5-5" },
        ],
        Slots =
        [
            new PageSlotView
            {
                Slot = 1,
                Type = "worker",
                Enabled = true,
                WorkerName = "opus",
                Model = "claude-opus-4",
                Categories = ["A"],
                Card = "070-A-config-model.md",
                Start = Built.AddMinutes(-12),
                LogTail = ["thinking about the config", "editing Cfg.cs"],
                Stats = "1.2k tok/s",
            },
            new PageSlotView
            {
                Slot = 2,
                Type = "Sonnet",
                Enabled = true,
                WorkerName = "sonnet",
                Model = "gpt-5",
                Categories = ["C"],
                Card = "090-C-page-views.md",
                Start = Built.AddMinutes(-4),
                StopWhy = "asked to stop at a safe point",
            },
        ],
        Waiting =
        [
            new PageWaitingItem { Kind = PageWaitingKind.HeldCategory, Name = "B", Note = "lane B's definition changed with 2 cards pending" },
            new PageWaitingItem { Kind = PageWaitingKind.Stuck, Name = "030-A", Note = "3 runs ended without a result" },
            new PageWaitingItem
            {
                Kind = PageWaitingKind.Question,
                Name = "020-B",
                Note = "found a missing field",
                Questions =
                [
                    new CardQuestion
                    {
                        Number = 1,
                        Text = "Which format?",
                        Choices = [new CardChoice { Label = "json" }, new CardChoice { Label = "yaml" }],
                    },
                ],
            },
            new PageWaitingItem { Kind = PageWaitingKind.Waiting, Name = "320-A", Note = "waiting: developer" },
            new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "070-A", Note = "check the banner reads well" },
            new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "080-B", Note = "the git retry needs a look" },
        ],
        Planner = new PagePlannerView
        {
            OpenRequests =
            [
                new JobRequest { Path = "docs/jobs/requests/12.md", Name = "12.md", Title = "Ask for a scope plan", For = "the pool", Open = true },
            ],
            WaitingCards = ["060-E", "320-A"],
            AllDone = true,
        },
        Queue =
        [
            new PageQueueItem { Name = "100-A-lanes-and-workers-json.md", Title = "lanes and workers.json", Note = "next up" },
            new PageQueueItem { Name = "120-A-dispatch.md", Title = "dispatch", Note = "waiting on 100-A" },
        ],
        Events =
        [
            .. Enumerable.Range(1, 12).Select(n => new PageEvent
            {
                Time = Built.AddMinutes(-n),
                Text = $"finished {n:D2}-A-card-{n:D2} done",
                Tone = PageTone.Ok,
            }),
        ],
    };

    /// <summary>The word the old page shows for each state (L3528).</summary>
    public static string StateWord(SchedulerState state) => state switch
    {
        SchedulerState.Running => "running",
        SchedulerState.Paused => "paused",
        SchedulerState.PausedRed => "paused: main is red",
        SchedulerState.PausedQuota => "paused: quota used up",
        SchedulerState.Draining => "draining",
        _ => state.ToString().ToLowerInvariant(),
    };
}
