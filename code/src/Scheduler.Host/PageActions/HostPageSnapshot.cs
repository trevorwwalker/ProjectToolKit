using System.Globalization;
using Scheduler.Core.Accounting;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Health;
using Scheduler.Core.Categories;
using Scheduler.Core.Launch;
using Scheduler.Core.PageModel;
using Scheduler.Core.Tick;
using Scheduler.Core.Watchdog;
using Scheduler.Core.WebContract;
using Scheduler.Host.State;
using Scheduler.Web.Server;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The snapshot every page window is built from (PAGE-5), made on the scheduler thread from the
/// tick, which owns the state the page actions change, and from what Host keeps beside it
/// (<see cref="PageSnapshotSources"/>). Each window comes from Core's own builder: the queue from
/// <see cref="PageQueue"/> (PAGE-6), Waiting on you from <see cref="PageWaiting"/>, the stats from
/// <see cref="CardStats"/> (ACCT-6).
/// </summary>
public static class HostPageSnapshot
{
    private const int RecentShown = 10;

    // The old page's threshold for ", quiet q min" (schedule.ps1 L2380).
    private const int QuietShownAfter = 5;

    /// <summary>The snapshot as the tick stands now.</summary>
    public static PageSnapshot Build(SchedulerConfig config, IClock clock, SchedulerTick tick, PageSnapshotSources? sources = null)
    {
        sources ??= new PageSnapshotSources();
        var now = clock.Now;
        var running = tick.Running.Values.OrderBy(w => w.Slot).ToList();
        var plan = tick.LastPlan;
        var finished = sources.FinishedWithoutUpdate?.Invoke() ?? [];
        // 990-B: the moves the page's actions committed, shown before the tick's next card read.
        var all = sources.Overlay?.Apply(tick.Cards) ?? tick.Cards;
        var cards = new Dictionary<string, Card>(Text.Comparer);
        foreach (var card in all)
        {
            cards.TryAdd(card.Name, card);
        }

        var recent = sources.Recent?.Invoke() ?? [];
        var runs = sources.Runs?.Invoke() ?? [];
        var push = sources.PlannerPush?.Invoke();
        var models = sources.Models?.Invoke();
        // Roadmap item 4 (640-A): what the areas' finished runs really took (accounting.csv), and
        // the slots as dispatch sees them, so each queue row can name the worker type its card is
        // headed for and be measured against that model's history.
        var areaTypicals = AreaTypicals(config.LogDir, now);
        var queueTypes = QueueTypes(config, tick, models);
        IReadOnlyList<ModelEntry>? modelEntries = models is null
            ? null
            : [.. models.Select(m => new ModelEntry { Key = m.Key, Short = m.Value.FirstOrDefault() ?? "" })];
        // Roadmap item 1 (580-A): the copies the project edited, which wait under Waiting on you for
        // Overwrite or Keep. The snapshot names the project they belong to.
        var templates = (sources.EditedTemplates?.Invoke() ?? [])
            .Select(e => new PageTemplateEdit(config.Project, e.Target))
            .ToList();
        var categoryNames = (tick.Categories.Current?.Categories ?? new Dictionary<string, CategoryDefinition>())
            .OrderBy(l => l.Key, StringComparer.Ordinal)
            .ToDictionary(l => l.Key, l => l.Value.Name, Text.Comparer);

        return new PageSnapshot
        {
            Built = now,
            Project = config.Project,
            Version = SchedulerVersion.Text,
            Theme = config.PageTheme,
            State = State(tick.State),
            Notice = sources.Notice?.Current is { } notice ? string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss}  {1}", notice.At, notice.Text) : "",
            HostLoad = tick.Load,
            Health = Health(sources.Health, tick, now),
            Stats = Stats(runs, all, now, sources, models),
            PlannerLine = push is { } on ? $"Planner push: {(on ? "on" : "off")}" : "",
            PlannerPush = push,
            Categories = PageCategories.From(tick.Categories.Current),
            SharedAddOnly = tick.Categories.Current?.AddOnly ?? [],
            Slots = Slots(config, tick, models, categoryNames, cards, now, sources.Stats),
            WorkerTypes = WorkerTypes(config, tick, sources, models),
            Waiting = [.. PageWaiting.Window(all, plan?.Stuck, tick.Categories.Held, running, config.MaxRetries, finished, templates, StuckRuns(plan?.Stuck, tick, recent)).Select(w => Waiting(w, cards))],
            Planner = Planner(tick, all, plan),
            Queue = [.. PageQueue.Order(all, running, Notes(tick)).Select(q => Queued(q, cards, recent, areaTypicals, queueTypes, tick.Workers.Pool.Migrated, modelEntries, config))],
            Events = Events(recent),
            Trends = TrendDays(sources, runs, models, now),
        };
    }

    // 510-B: a waiting card's title, and where its note came from (CARD-13), for the tooltip and
    // the "Result:" marker. A held category is not a card and keeps neither.
    private static PageWaitingItem Waiting(PageWaitingItem item, Dictionary<string, Card> cards) =>
        item.Kind != PageWaitingKind.HeldCategory && cards.TryGetValue(item.Name, out var card)
            ? item with { Title = card.Title, NoteFrom = card.Why.Length > 0 && (item.Note == card.Why || item.Note == $"Result: {card.Why}") ? card.WhyFrom : "" }
            : item;

    // 946-B: what each stuck card's runs did, for its Waiting on you row (DISP-7, ACCT-5). The count is
    // the one Host keeps against the card (TIME-22); the last run is the newest of that card's finished
    // runs, which names the result the row shows and the log its link opens. A recent row that names the
    // card by its id rather than its file name still matches.
    private static IReadOnlyList<PageStuckRun>? StuckRuns(
        IReadOnlyCollection<Card>? stuck,
        SchedulerTick tick,
        IReadOnlyList<RecentEvent> recent)
    {
        if (stuck is not { Count: > 0 })
        {
            return null;
        }

        var runs = new List<PageStuckRun>(stuck.Count);
        foreach (var card in stuck)
        {
            var last = recent.FirstOrDefault(r => Text.Same(r.Card, card.Name) || Text.Same(r.Card, card.Id));
            runs.Add(new PageStuckRun
            {
                Card = card.Id,
                Runs = tick.Retries.TryGetValue(card.Id, out var count) ? count : 0,
                Result = last?.Result ?? "",
                Log = last?.Log ?? "",
            });
        }

        return runs;
    }

    // 510-B: a queued card's Depends on list, the log of its newest run (accounting's recent
    // runs are newest first), and its time-box words (640-A).
    private static PageQueueItem Queued(
        PageQueueItem item,
        Dictionary<string, Card> cards,
        IReadOnlyList<RecentEvent> recent,
        IReadOnlyList<AreaTypical> typicals,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        IReadOnlyList<ModelEntry>? modelEntries,
        SchedulerConfig config) => item with
    {
        Depends = cards.TryGetValue(item.Name, out var card) ? card.Depends : [],
        Log = recent.FirstOrDefault(r => Text.Comparer.Equals(r.Card, item.Name) && !string.IsNullOrEmpty(r.Log))?.Log ?? "",
        TimeBoxNote = TimeBoxNote(cards.GetValueOrDefault(item.Name), typicals, types, migrated, modelEntries, config),
    };

    // Roadmap item 4 (640-A): the time-box warning or suggestion for one queued card. The card goes
    // to the worker dispatch would give it (DISP-17, DISP-18); the accounting rows' model column is
    // what ModelColumn.Resolve wrote for that worker, so the same resolve names the right figures.
    private static string TimeBoxNote(
        Card? card,
        IReadOnlyList<AreaTypical> typicals,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        IReadOnlyList<ModelEntry>? modelEntries,
        SchedulerConfig config)
    {
        if (card is null || typicals.Count == 0)
        {
            return "";
        }

        var slot = TypeDispatcher.Fitting(types, card, migrated);   // WT-12
        if (slot is null)
        {
            return "";
        }

        var model = ModelColumn.Resolve(modelEntries ?? [], slot.Model, slot.Endpoint, config.Model);
        return TimeBoxAdvice.Text(card.TimeBox, AreaDurations.For(typicals, card.Category, model));
    }

    // Roadmap item 4 (640-A): what each area's finished runs really took, from accounting.csv's own
    // lines; nothing when the file cannot be read, which leaves the queue without time-box words.
    private static IReadOnlyList<AreaTypical> AreaTypicals(string logDir, DateTimeOffset now)
    {
        var rows = AccountingRows(logDir);
        return rows is { Length: > 0 } ? AreaDurations.Typical(AreaDurations.RunsFromRows(rows, now)) : [];
    }

    // The types as dispatch sees them (WT-3, WT-12), for the type a queued card is headed for. Nothing
    // is busy or out of quota here: which type a card would get says nothing about whether it has room.
    private static List<DispatchType> QueueTypes(
        SchedulerConfig config,
        SchedulerTick tick,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models) =>
    [
        .. Tick.TickHost.PoolOf(tick, config).Types.Select(t =>
        {
            var e = WorkerSelection.Effective(config, t);
            return new DispatchType
            {
                Name = t.Name,
                Max = t.Max,
                Model = e.Model,
                ModelNames = models?.GetValueOrDefault(e.Model) ?? [],
                IsDefault = tick.Types.IsDefault(t.Name),
                Endpoint = e.Endpoint,
                Agent = t.Agent,
                Slots = t.Slots,
            };
        }),
    ];

    // WT-P3: the Worker types panel's rows, in config order, with how many run and, for an agent out
    // of quota, since when (WT-9).
    // The Worker types panel's rows (WT-C7, 974-B). Until main's workers.json has been read the panel
    // has no types to show: the tick's are the ones the slots migrate to (WT-C10.4), and showing them
    // was how every restart answered a saved name with "worker".
    private static List<PageWorkerTypeView> WorkerTypes(
        SchedulerConfig config,
        SchedulerTick tick,
        PageSnapshotSources sources,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models) =>
        sources.WorkersFromMain?.Invoke() == false
            ? []
            : [.. Tick.TickHost.PoolOf(tick, config).Types.Select(t => new PageWorkerTypeView
            {
                Name = t.Name,
                Running = tick.Running.Values.Count(w => Text.Same(w.Type, t.Name)),
                Max = t.Max,
                Agent = t.Agent,
                Model = t.Model,
                // 1087-C (WT-P3): the model the type runs when its own is blank, named as the worker
                // windows name a running worker's; the panel shows it greyed as the box's placeholder.
                EffectiveModel = WorkerSelection.DisplayedModel(config, t, models),
                Endpoint = t.Endpoint,
                MetricsUrl = t.MetricsUrl,
                IsDefault = tick.Types.IsDefault(t.Name),
                Projects = t.Projects,
                OutOfQuotaSince = tick.Quota.Since(WorkerSelection.Effective(config, t).Agent),
            })];

    /// <summary>
    /// The live view's workers (PAGE-3): each running worker's readable log, a stats line naming
    /// its model and endpoint (PAGE-10), and how long it has run.
    /// </summary>
    public static IReadOnlyList<LiveWorker> Live(SchedulerConfig config, IClock clock, SchedulerTick tick, Func<int, WorkerStats?>? stats = null) =>
    [
        .. tick.Running.Values.OrderBy(w => w.Slot).Select(w => new LiveWorker(
            w.Slot,
            w.Log,
            WorkerStatsText.Line(stats?.Invoke(w.Slot), w.Model, w.Endpoint),
            // 510-B: minutes run out of the time limit, as status.json writes it, so the page's
            // time-limit bar fills; and, as the old page did, ", quiet q min" once the log has not
            // grown for 5 minutes, which stops the spinner.
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1} min{2}",
                Math.Max(0, (int)(clock.Now - w.Start).TotalMinutes),
                WatchdogRules.Limit(w.TimeBox, config),
                Quiet(w, clock.Now) is { } quiet ? $", quiet {quiet} min" : ""))),
    ];

    /// <summary>The minutes since <paramref name="w"/>'s log last grew (TIME-8), once 5 or more, else null (510-B, schedule.ps1 L2380).</summary>
    public static int? Quiet(RunningWorker w, DateTimeOffset now)
    {
        var since = w.LastProgress == default ? w.Start : w.LastProgress;
        var minutes = (int)(now - since).TotalMinutes;
        return minutes >= QuietShownAfter ? minutes : null;
    }

    // WRK-14, 700-B: the time the hook handed this worker the developer's message, read off the stamp
    // MessageDelivery.Run puts on msg-slotN.read.txt (last-write time, kept in local time). Null when
    // the file is not there or cannot be read.
    private static DateTimeOffset? MessageRead(SchedulerConfig config, int slot)
    {
        var path = WorkerFiles.MessageReadFile(config.LogDir, slot);
        try
        {
            return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTime(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // PAGE-5: one window per worker slot. 510-B: with its time limit and kill time, its model's
    // short name, the running card's title and category name, quiet minutes, a message not
    // read yet, and whether an idle worker's model server answers.
    private static List<PageSlotView> Slots(
        SchedulerConfig config,
        SchedulerTick tick,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models,
        Dictionary<string, string> categoryNames,
        Dictionary<string, Card> cards,
        DateTimeOffset now,
        Func<int, WorkerStats?>? stats = null)
    {
        var slots = new List<PageSlotView>();
        var starting = tick.Starting;   // 995-B: slots whose claim is on its way to main show their card as starting
        foreach (var slot in Enumerable.Range(1, tick.Workers.Slots).Union(tick.Running.Keys).Union(starting.Keys).Order())   // WT-W5: a folder above maxWorkers is listed while it runs
        {
            var slotWorker = WorkerSelection.Effective(config, tick.Workers.Workers, slot);
            if (!tick.Running.TryGetValue(slot, out var running))
            {
                starting.TryGetValue(slot, out running);
            }

            // 1097-B: the live worker for this slot, if it has one (a starting claim is not yet live).
            tick.Running.TryGetValue(slot, out var live);

            var isStarting = running is not null && running.Pid == 0 && starting.ContainsKey(slot);
            // 959-A: a running row is the worker, not the folder (WT-W1): its model and its short name
            // come from the type it was started as (WT-C4), not from whatever
            // worker the folder number was given. An idle folder still shows its own slot worker.
            var worker = running is null ? slotWorker : TypeWorker(config, tick, running, slotWorker);
            var card = running is null ? null : cards.GetValueOrDefault(running.Card);
            slots.Add(new PageSlotView
            {
                Slot = slot,
                Enabled = tick.Workers.Enabled.GetValueOrDefault(slot, true),
                WorkerName = worker.Name,
                Type = running?.Type ?? "",
                Model = worker.Model,
                Categories = worker.Categories.Length == 0 ? [] : [worker.Categories],
                Card = running?.Card ?? "",
                Start = running?.Start,
                // 1097-B (PAGE-10): the same stats line the live view gives this slot, so a freshly drawn
                // page shows it at once instead of "- T/s" until /live.json answers. PageVersion strips
                // Stats, so a stats change never reloads the page.
                Stats = live is null ? "" : WorkerStatsText.Line(stats?.Invoke(slot), live.Model, live.Endpoint),
                StopWhy = running?.StopWhy ?? "",
                TimeLimit = running is null ? null : WatchdogRules.Limit(running.TimeBox, config),
                KillAt = running?.StopAt is { } stopAt ? stopAt.AddMinutes(running.StopGraceMinutes != 0 ? running.StopGraceMinutes : config.StopGraceMinutes) : null,
                ModelShort = models?.GetValueOrDefault(worker.Model)?.FirstOrDefault() ?? "",
                CardTitle = isStarting ? "starting (claim on its way to main): " + (card?.Title ?? "") : card?.Title ?? "",
                CategoryName = running is null ? "" : categoryNames.GetValueOrDefault(running.Category) ?? "",
                QuietMinutes = running is null ? null : Quiet(running, now),
                MessagePending = running is not null && File.Exists(WorkerFiles.MessageFile(config.LogDir, slot)),
                MessageReadAt = running is null ? null : MessageRead(config, slot),
                ServerDown = running is null && worker.Endpoint.Length > 0 && tick.Servers.KnownDown(worker.Endpoint),
            });
        }

        return slots;
    }

    // 959-A: the worker a running folder's row describes - the type it was started as (WT-W5 keeps the
    // name on the worker), with the model, endpoint and agent it really started with on top of that
    // type's settings. A worker whose type has gone from the config keeps the slot worker's name and
    // areas, since nothing else can say what the folder was.
    private static EffectiveWorker TypeWorker(
        SchedulerConfig config,
        SchedulerTick tick,
        RunningWorker running,
        EffectiveWorker slotWorker)
    {
        var type = Tick.TickHost.PoolOf(tick, config).Types.FirstOrDefault(t => Text.Same(t.Name, running.Type));
        var baseWorker = type is null ? slotWorker : WorkerSelection.Effective(config, type);
        return baseWorker with
        {
            Model = running.Model.Length > 0 ? running.Model : baseWorker.Model,
            MetricsUrl = running.MetricsUrl.Length > 0 ? running.MetricsUrl : baseWorker.MetricsUrl,
            Agent = running.Agent.Length > 0 ? running.Agent : baseWorker.Agent,
        };
    }

    // PAGE-8: the word the state shows as.
    private static PageStateView State(SchedulerState state) => new()
    {
        State = state,
        Text = state switch
        {
            SchedulerState.Running => "running",
            SchedulerState.Paused => "paused",
            SchedulerState.PausedRed => "paused: main is red",
            SchedulerState.PausedQuota => "paused: out of quota",
            SchedulerState.Draining => "draining",
            _ => state.ToString().ToLowerInvariant(),
        },
        Tone = state == SchedulerState.Running ? PageTone.Ok : PageTone.Warn,
    };

    // HLTH-5, HLTH-6, PAGE-5: the health line as schedule.ps1 wrote it (L3540-3543), the sha the
    // banner names, server-down and the category overlaps. The banner words are the theme's (PAGE-11).
    private static PageHealthView Health(HealthCheck? health, SchedulerTick tick, DateTimeOffset now)
    {
        var view = new PageHealthView
        {
            ServerDown = tick.Servers.Down().Count > 0,
            DownServers = tick.Servers.Down(),
            CategoryOverlaps = tick.Categories.Overlaps,
        };
        if (health is null)
        {
            return view with { Text = "no health check set" };
        }

        var text = health.LastWhen is { } when
            ? string.Format(
                CultureInfo.InvariantCulture,
                "main {0} {1} at {2:HH:mm}",
                health.LastSha,
                health.LastResult == HealthResult.Red ? "RED" : "green",
                when)
            : "main not checked yet";
        if (health.Running && health.Started is { } started)
        {
            text += string.Format(CultureInfo.InvariantCulture, "; checking {0} ({1} min)", health.Sha, (int)(now - started).TotalMinutes);

            // 1100-B (HLTH-11): the time-box warning sits in the line while the check that earned it runs.
            if (health.Warning is { Length: > 0 } warn)
            {
                text += "; " + warn;
            }
        }

        return view with
        {
            Green = health.LastResult != HealthResult.Red,
            Text = text,
            Sha = health.LastSha,
            HasLog = health.LogPath.Length > 0 && File.Exists(health.LogPath),
        };
    }

    // ACCT-6: the last 24 hours of accounting's runs, by card; 1095-C: and the same for each range the
    // page offers, with that range's Trends buckets, built in one go from the 14 days of rows the
    // Trends window already reads (a file it cannot read leaves the Control window's own rows).
    private static PageStats Stats(
        IReadOnlyList<RunStat> runs,
        IReadOnlyList<Card> cards,
        DateTimeOffset now,
        PageSnapshotSources sources,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models)
    {
        var entries = CardStats.GroupByCard(runs, cards, now);
        var wide = sources.TrendRuns?.Invoke() ?? runs;
        var modelEntries = models?.Select(m => new ModelEntry { Key = m.Key, Short = m.Value.FirstOrDefault() ?? "" }).ToList();
        var ranges = new List<PageStatsRange>(CardStats.Ranges.Count);
        foreach (var range in CardStats.Ranges)
        {
            var inRange = CardStats.GroupByCard(wide, cards, now, range.Window);
            ranges.Add(new PageStatsRange
            {
                Key = range.Key,
                Cards = inRange.Count,
                Minutes = inRange.Sum(e => e.Minutes),
                Text = string.Join("\n", CardStats.Summary(wide, cards, now, modelEntries, range.Window)),
                Trend = Trends.Range(wide, now, range, modelEntries),
            });
        }

        return new PageStats
        {
            Cards = entries.Count,
            Minutes = entries.Sum(e => e.Minutes),
            Text = string.Join("\n", CardStats.Summary(runs, cards, now)),
            Ranges = ranges,
        };
    }

    // Roadmap item 3 (630-A): the last 14 local days by model for the Trends window, or nothing at
    // all when there is less than one day of data - the page hides the window on an empty list.
    // The rows come from accounting (770-B): the file read and its cache are AccountingCsv's, and a
    // file it cannot read leaves the chart on the Control window's figures, which only reach 48 h
    // behind the start (ACCT-4). models.json's first name for a model is the short one the chart
    // labels it with (DISP-18).
    private static IReadOnlyList<TrendDay> TrendDays(
        PageSnapshotSources sources,
        IReadOnlyList<RunStat> recentRuns,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models,
        DateTimeOffset now)
    {
        var runs = sources.TrendRuns?.Invoke() ?? recentRuns;
        var entries = models?.Select(m => new ModelEntry { Key = m.Key, Short = m.Value.FirstOrDefault() ?? "" }).ToList();
        var days = Trends.Daily(runs, now, models: entries);
        return Trends.HasTrend(days) ? days : [];
    }

    // accounting.csv's lines for the queue's time-box words (640-A), kept from one snapshot to the
    // next: 1116-B, read on from where the last read ended as the file grows, and not at all while it
    // stands still (AccountingLines). A file that cannot be read is no lines at all, which leaves the
    // queue without those words.
    // (The Trends window stopped reading the file here in 770-B; that read is AccountingCsv's.)
    private static readonly AccountingLines Rows = new();

    private static string[]? AccountingRows(string logDir)
    {
        if (string.IsNullOrWhiteSpace(logDir))
        {
            return null;
        }

        var path = Path.Combine(logDir, "accounting.csv");
        try
        {
            return Rows.Read(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // PAGE-5, REQ: the open requests, and the cards waiting on the planner with "all done" once
    // every card they depend on is finished (schedule.ps1 L3610-3618).
    private static PagePlannerView Planner(SchedulerTick tick, IReadOnlyList<Card> all, DispatchPlan? plan)
    {
        var waiting = all.Where(CardWaits.OnPlanner).OrderBy(c => c.Num).ToList();
        var finished = plan?.Finished ?? new HashSet<string>(Text.Comparer);
        return new PagePlannerView
        {
            OpenRequests = [.. tick.Requests.Where(r => r.Open)],
            WaitingCards = [.. waiting.Select(c => c.Name)],
            AllDone = waiting.Count > 0 && waiting.All(c => c.Depends.Count > 0 && c.Depends.All(finished.Contains)),
            WaitingDepends = waiting.ToDictionary(c => c.Name, c => c.Depends, Text.Comparer),
            WaitingReady = [.. waiting.Where(c => c.Depends.Count > 0 && c.Depends.All(finished.Contains)).Select(c => c.Name)],
        };
    }

    // PAGE-6, DISP-19 (750-B): the note per card, the first where a card has more than one. The
    // dispatcher's result is used once there has been a dispatch, because it holds the plan's notes
    // (waits for, overlaps, stuck) after its own not-started notes; before the first dispatch of a
    // run there is only the plan. Without the dispatcher's half a ready card that no worker took
    // showed nothing but its Status.
    private static Dictionary<string, string> Notes(SchedulerTick tick)
    {
        var notes = new Dictionary<string, string>(Text.Comparer);
        foreach (var note in tick.LastDispatch?.Notes ?? tick.LastPlan?.Notes ?? [])
        {
            notes.TryAdd(note.Card, note.Note);
        }

        return notes;
    }

    // PAGE-5, ACCT-5: the last 10 finished runs, toned as schedule.ps1 did (L3691).
    private static List<PageEvent> Events(IReadOnlyList<RecentEvent> recent) =>
    [
        .. recent.Take(RecentShown).Select(r => new PageEvent
        {
            Time = r.When,
            Text = string.Format(CultureInfo.InvariantCulture, "slot {0}  {1}: {2}, {3} min", r.Slot, r.Card, r.Result, r.Minutes),
            Slot = r.Slot,
            Type = r.Type,
            Model = r.Model,
            Card = r.Card,
            Result = r.Result,
            Minutes = r.Minutes,
            Log = r.Log ?? "",
            Tone = r.Result switch
            {
                "done" or "review" => PageTone.Ok,
                "stopped" or "stopped from page" or "killed" or "waiting" or "needs-human" => PageTone.Warn,
                _ => PageTone.Bad,
            },
        }),
    ];
}
