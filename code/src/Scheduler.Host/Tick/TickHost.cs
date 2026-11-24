using System.Text;
using System.Text.Json;
using Scheduler.Core.Accounting;
using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Categories;
using Scheduler.Core.Launch;
using Scheduler.Core.MainModel;
using Scheduler.Core.Results;
using Scheduler.Core.Scopes;
using Scheduler.Core.Watchdog;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Planner;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.Templates;
using ClaimState = Scheduler.Core.Tick.ClaimState;
using IClaimingHost = Scheduler.Core.Tick.IClaimingHost;

namespace Scheduler.Host.Tick;

/// <summary>
/// Host's side of the tick (spec A3): <see cref="ITickHost"/> made of the existing parts - cards
/// from main (040-B), slots (110-B), processes and results (240-B), the planner (260-B) - and
/// Core's rules (Launch 160-A, Watchdog 170-A, Results 180-A). It adds no rule of its own.
/// </summary>
public sealed class TickHost : ITickHost, IClaimingHost, Scheduler.Core.Tick.ILockingHost
{
    /// <summary>995-B: a claim that is still not committed after this many writes is given up (the slot is freed).</summary>
    public const int ClaimTries = 5;

    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, PostedClaim> _claims = new();
    private readonly TickHostParts _p;
    private readonly TickStateWriter _state;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _onTick = new();
    private readonly MainFetcher _fetcher;
    private FetchAsk _ask = FetchAsk.Poll;   // the wake of the tick about to run, as the fetch gate names it
    private readonly RequestReader _requests;
    private DateTimeOffset? _statusLogged;
    private IMainFeed? _feed;
    private readonly Action _fetchMoved;

    /// <summary>The key the planner step is posted under: one waiting is enough (987-B).</summary>
    public const string PlannerKey = "planner step";

    /// <summary>A host over the given parts.</summary>
    public TickHost(TickHostParts parts)
    {
        _p = parts;
        Writes = PlannerWrites;
        _state = new TickStateWriter(parts.Files, parts.Clock);
        _fetcher = new MainFetcher(parts.Main, parts.Clock, FetchInterval(parts.Config));
        _requests = new RequestReader(parts.Main, parts.Config.JobsDir);
        _fetchMoved = () => parts.TickNow?.Invoke();
        Logs = new WorkerLogRenderer(parts.Clock);
    }

    /// <summary>Each running worker's readable log and live stats (460-B, PAGE-10).</summary>
    public WorkerLogRenderer Logs { get; }

    /// <summary>The tick this host serves; set once the tick exists (the tick takes the host in its constructor).</summary>
    public Scheduler.Core.Tick.SchedulerTick? Tick { get; set; }

    /// <summary>The planner step, or null when no planner is configured (PLAN-1).</summary>
    public PlannerStep? Planner { get; set; }

    /// <summary>1111-B: numbers the new-* cards found on main, beside the planner step; null in an observe or dry run.</summary>
    public NewCardStep? NewCards { get; set; }

    /// <summary>The generic files kept in step with ProjectToolkit's templates, or null when no toolkit was found (card 540-B).</summary>
    public TemplateRefresh? Templates { get; set; }

    /// <summary>The card move requests (card 660-B), or null in an observe or dry run.</summary>
    public CardRequestStep? Moves { get; set; }

    /// <summary>
    /// 1063-B: the repository's model of main (state-cache.md MM-8, MM-9, MM-14). Once it has loaded, the tick's
    /// reads of main - cards, requests, settings files, main's commit - come from it, its ref read in process,
    /// and every fetch is its gate's; until then they go through the git CLI as before. Set once the model exists.
    /// </summary>
    public IMainFeed? Feed
    {
        get => _feed;
        set
        {
            // 1124-B: a background fetch that moves main wakes this project's tick (as other wakes do).
            if (_feed is not null)
            {
                BackgroundFetch.For(_feed).Moved -= _fetchMoved;
            }

            if (value is not null)
            {
                BackgroundFetch.For(value).Moved += _fetchMoved;
            }

            _feed = value;
            _fetcher.Feed = value;
            _requests.Feed = value;
        }
    }

    /// <summary>WT-P2: sets a card waiting on you once its worker, stopped or killed from the page, is gone.</summary>
    public StoppedCardHold? StoppedHold { get; set; }

    /// <inheritdoc />
    public void IndexLogs() => Directory.CreateDirectory(_p.Config.LogDir);

    /// <summary>
    /// 982-B: main's commit as of the last fetch (a rev-parse of the remote ref, no fetch), so a light
    /// wake costs one local git call; null when git failed, which makes the tick full.
    /// </summary>
    public string? MainSha()
    {
        if (_feed is { Model: { } model } feed)
        {
            _ = feed.Tick();   // 1063-B (MM-8): the ref read in process, no git
            return model.Commit;
        }

        return _p.Main.HeadSha();
    }

    // 1063-B: a file at main, from the model once it has loaded (MM-6: then kept current), else through git.
    private string? ReadMain(string path) =>
        _feed?.Model is { Snapshot: not null } model ? model.Settings(path) : _p.Main.ReadFile(path);

    private bool MainKnown() => _feed?.Model is { Snapshot: not null } || _p.Main.HeadSha() is not null;

    /// <summary>
    /// 1002-B: the wake of the tick about to run, so the card read knows whether to fetch: a click
    /// or a move request reads the local remote ref. Without it the fetch follows the poll interval.
    /// </summary>
    public void Woke(Scheduler.Core.Tick.TickWake wake)
    {
        _fetcher.Woke(wake);
        _ask = MainFetcher.AskFor(wake);   // the template step's fetch follows the same rule (MM-9)
    }

    // The poll comes every PollSeconds counted from a tick, so a little less still fetches on each poll.
    internal static TimeSpan FetchInterval(SchedulerConfig config) =>
        TimeSpan.FromSeconds(Math.Max(1, config.PollSeconds - 2));

    /// <inheritdoc />
    public IReadOnlyList<Card>? ReadCards()
    {
        if (!_fetcher.Fetch())
        {
            return null; // CARD-3: keep the previous list
        }

        return _p.Reader.Read();
    }

    /// <inheritdoc />
    public bool FixDuplicates(IReadOnlyList<Card> cards)
    {
        var duplicate = cards.GroupBy(c => c.Id, StringComparer.Ordinal)
            .Any(g => g.Any(c => c.Folder == CardFolder.Queue) && g.Any(c => c.Folder != CardFolder.Queue));
        if (!duplicate)
        {
            return false;
        }

        _p.Reader.ReadFixingDuplicates(_p.Committer);
        return true;
    }

    /// <inheritdoc />
    public void PlannerStep()
    {
        // 987-B: what the writer handed back for the tick's own thread (the planner's retry releases).
        while (_onTick.TryDequeue(out var landed))
        {
            landed();
        }

        // 987-B: the writes go to the git writer and the tick carries on; one that moves main wakes it.
        if (Writer is { } writer)
        {
            writer.Post(PlannerKey, Writes);
            return;
        }

        if (Writes())
        {
            _p.TickNow?.Invoke();
        }
    }

    /// <summary>987-B: the one writer to main, or null to write on the tick's thread (tests, dry run).</summary>
    public GitWriter? Writer { get; set; }

    /// <summary>
    /// 987-B: the planner step's writes - move requests, templates, planner batches - run on the
    /// writer; true when one moved main. A test may replace it.
    /// </summary>
    public Func<bool> Writes { get; set; }

    /// <summary>987-B: runs <paramref name="action"/> on the tick's thread at the next planner step.</summary>
    public void OnTick(Action action) => _onTick.Enqueue(action);

    /// <summary>Dynamic locks (DL-20): the repository's lock table file; null keeps no table (an observe or dry run).</summary>
    public LockTableFile? LockFile { get; set; }

    /// <summary>Dynamic locks (section 6): the step that answers <c>.lock</c> requests; null answers none.</summary>
    public Scheduler.Host.Locks.LockRequestStep? LockRequests { get; set; }

    /// <summary>
    /// Dynamic locks (DL-19, 1025-B): the audit of main, run in a finish before the worker's moves are applied,
    /// so a stray card's To: done becomes To: review in the same finish; null (or locks off) runs none there.
    /// </summary>
    public Scheduler.Host.Locks.AuditRunner? Audit { get; set; }

    /// <summary>Dynamic locks (section 3): the full path of the exe whose <c>lock</c> command workers run; "" gives them none.</summary>
    public string LockExe { get; set; } = "";

    /// <summary>True when the tick has locks on and this host keeps a table.</summary>
    public bool LocksOn => Tick?.Locks.On == true && LockFile is not null;

    /// <inheritdoc />
    public Scheduler.Core.Locks.LockTable ReadLocks() =>
        LockFile?.Read() ?? Scheduler.Core.Locks.LockTable.Empty(_p.Config.Repo);

    /// <inheritdoc />
    public void SaveLocks(Scheduler.Core.Locks.LockTable table) => LockFile?.Save(table);

    /// <inheritdoc />
    public void LockStep()
    {
        ReleaseMoved();   // DL-8: releases from applied moves come before the requests are answered
        var tick = Tick;
        if (LockRequests is not { } step || tick is null)
        {
            return;
        }

        var categories = tick.Categories.Current;
        var rules = new Scheduler.Core.Locks.LockRules
        {
            AddOnly = categories?.AddOnly ?? [],
            CategoryDefaults = categories is null ? [] : [.. categories.Categories.Values.Select(c => c.Owns)],
            WaitLimit = tick.Locks.WaitLimit,
        };
        step.Run(
            rules,
            slot => tick.Running.TryGetValue(slot, out var w) ? w.Card : null,
            card => tick.Running.Values.FirstOrDefault(w => Scheduler.Core.Contracts.Text.Same(w.Card, card)) is { } w ? WarnAt(w) : null);
    }

    /// <summary>
    /// 1020-B: told by the move step (on the writer's thread) that a worker's move request was filed;
    /// the lock table is changed on the tick's own thread, at the next lock step or run end.
    /// </summary>
    public void MoveFiled(Scheduler.Core.Moves.CardRequest request, bool applied)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (LocksOn)
        {
            Releases?.MoveFiled(request.Card, request.To, applied);
        }
    }

    private LockReleases? _releases;

    /// <summary>The releases (DL-8, DL-9) over <see cref="LockFile"/>; null without one.</summary>
    private LockReleases? Releases => LockFile is not { } file ? null : _releases ??= new LockReleases(
        file,
        card => Tick?.Running.Values.Any(w => Scheduler.Core.Contracts.Text.Same(w.Card, card)) == true,
        card => Moves?.HasPending(card) == true,
        _p.Log);

    /// <summary>DL-13: a running card's warn time - a wait never runs past it.</summary>
    private DateTimeOffset WarnAt(RunningWorker w)
    {
        var limit = WatchdogRules.Limit(w.TimeBox, _p.Config);
        return w.Start.AddMinutes(limit - WatchdogRules.WarnLead(limit, _p.Config));
    }

    private void ReleaseMoved()
    {
        if (LocksOn)
        {
            Releases?.ReleaseMoved();
        }
    }

    private void ReleaseAtRunEnd(RunningWorker worker, string result)
    {
        if (LocksOn)
        {
            Releases?.RunEnded(worker.Card, result);
        }
    }

    private bool PlannerWrites()
    {
        // 540-B: beside the planner, as both commit to main through the answers worktree.
        // 1025-B (DL-19): a worker may push and ask To: done before its run ends, so with locks on a waiting
        // request is audited here too; locks off, or nothing waiting, adds no fetch.
        if (LocksOn && Moves is { } waiting && waiting.Pending().Count > 0)
        {
            AuditBeforeMoves();
        }

        var moved = Moves?.Run() > 0; // 660-B: requests left over from claims and finishes
        Templates?.Step(_ask);
        var numbered = NewCards?.Run() ?? false;   // 1111-B (b): new-* cards on main get the pool's numbers
        return (Planner is { } planner && planner.Run()) | moved | numbered;
    }

    /// <summary>
    /// Whether main's <c>workers.json</c> has been read since this scheduler started (WT-C7, 974-B),
    /// which is what says the Worker types panel may be shown and saved: until it is true the types the
    /// tick holds are the migration of the slots, not what main says. A main that can be read but has no
    /// workers.json counts as read - a project's first Save writes that file (WT-C11).
    /// </summary>
    public bool WorkersFromMain { get; internal set; }

    /// <inheritdoc />
    public string? ReadCategories() => CategoriesLocation.Read(_p.Config.JobsDir, ReadMain);

    /// <inheritdoc />
    public string? ReadWorkers()
    {
        var text = ReadMain($"{_p.Config.JobsDir}/workers.json");
        if (text is not null || MainKnown())
        {
            WorkersFromMain = true;
        }

        return text;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(Scheduler.Core.Contracts.Text.Comparer);
        var text = ReadMain($"{_p.Config.JobsDir}/models.json");
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                var names = new List<string>();
                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("short", out var s) && s.ValueKind == JsonValueKind.String
                    && s.GetString() is { Length: > 0 } shortName)
                {
                    names.Add(shortName);
                }

                result[entry.Name] = names;
            }
        }
        catch (JsonException)
        {
            // An unreadable models.json names no models.
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<JobRequest> ReadRequests()
    {
        return _requests.Read();   // 1002-B: nothing read when main has not moved, only new blobs when it has
    }

    /// <inheritdoc />
    public RunningWorker Watch(RunningWorker worker)
    {
        var now = _p.Clock.Now;
        _p.FinishedCards?.Observe(worker.Card);
        worker = worker with { RawPosition = Logs.Advance(worker, FormatOf(worker), false) };
        var size = _p.Processes.LogSize(worker.RawLog) ?? worker.LastSize;
        var step = WatchdogRules.Watch(worker, _p.Config, now, size);
        foreach (var line in step.Log)
        {
            _p.Log.Write(line);
        }

        if (step.WarnText is not null)
        {
            WriteFlag(WorkerFiles.WarnFlag(_p.Config.LogDir, worker.Slot), step.WarnText);
        }

        var result = step.Worker;
        if (step.Action == WatchAction.RequestStop)
        {
            WriteFlag(WorkerFiles.StopFlag(_p.Config.LogDir, worker.Slot), $"stop requested: {step.Reason}");
            _p.Log.Write($"slot {worker.Slot} stop requested ({worker.Card}): {step.Reason}");
        }
        else if (step.Action == WatchAction.Kill)
        {
            var kill = WatchdogRules.Kill(result, step.Reason, now);
            foreach (var line in kill.Log)
            {
                _p.Log.Write(line);
            }

            result = kill.Worker;
            if (kill.Action == WatchAction.Kill)
            {
                _p.Processes.EndJob(worker.Job, worker.Pid);
            }
        }

        return result;
    }

    // 1025-B (DL-19): what the worker pushed is on the remote only, so fetch main and audit it before the moves;
    // a fetch or audit that fails is logged and the moves are applied as before.
    private void AuditBeforeMoves()
    {
        if (!LocksOn || Audit is not { } audit)
        {
            return;
        }

        try
        {
            if (_feed is { Model: { } model } feed)
            {
                // 1063-B (MM-9): the gate's fetch, shared with the card read that follows the worker's end.
                var answer = feed.Fetch(FetchAsk.Audit);
                if (answer == FetchAnswer.Failed)
                {
                    _p.Log.Write("locks: audit before the moves could not fetch main; they are applied unaudited");
                    return;
                }

                if (answer != FetchAnswer.Fetched)
                {
                    _ = feed.Tick();
                }

                if (model.Commit is { } at)
                {
                    audit.Run(at);
                }

                return;
            }

            if (!_p.Main.Fetch())
            {
                _p.Log.Write("locks: audit before the moves could not fetch main; they are applied unaudited");
                return;
            }

            if (_p.Main.HeadSha() is { } head)
            {
                audit.Run(head);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _p.Log.Write($"locks: audit of main before the moves failed: {e.Message}");
        }
    }

    /// <inheritdoc />
    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries)
    {
        _fetcher.Ask();   // 1002-B: what the worker pushed is on the remote only; the next card read fetches
        // 660-B: the finish the worker asked for is on main before its card is checked; 987-B: in its
        // turn on the writer, so it never runs beside the planner step's own pass over the requests.
        if (Moves is { } finishMoves)
        {
            // 1025-B (DL-19): with locks on, main is fetched and audited first, in the same turn on the writer.
            int Apply()
            {
                AuditBeforeMoves();
                return finishMoves.Run();
            }

            _ = Writer is { } w ? w.Run("moves", Apply) : Apply();
        }
        var tick = Tick ?? throw new InvalidOperationException("TickHost.Tick is not set.");
        var format = FormatOf(worker);
        worker = worker with { RawPosition = Logs.Advance(worker, format, true) };
        Logs.Forget(worker.Slot);
        var readable = ReadLines(worker.Log);
        var raw = worker.RawLog == worker.Log ? readable : ReadLines(worker.RawLog);
        var agent = Agents.AgentOf(_p.Config, worker.Agent);   // WT-9: the finished run's own agent, whatever its slot
        var patterns = agent.QuotaPatterns.Count > 0 ? agent.QuotaPatterns : _p.Config.QuotaPatterns;
        var outcome = RunResults.Settle(
            worker,
            readable,
            raw,
            format != AgentFormat.Text, // TIME-14: in a rendered log only the model's own lines count
            patterns,
            () => worker.Endpoint.Length == 0 || _p.Probe.IsUp(worker.Endpoint),
            _p.Processes.ExitCode(worker.Pid),
            state == SchedulerState.Running,
            new Dictionary<string, int>(retries));

        retries.Clear();
        foreach (var (id, count) in outcome.Retries)
        {
            retries[id] = count;
        }

        var model = ModelColumn.Resolve([], worker.Model, worker.Endpoint, _p.Config.Model);
        var running = new Dictionary<int, RunningWorker> { [worker.Slot] = worker };
        var fromPage = StoppedCardHold.Applies(worker);
        var unupdated = _p.FinishedCards?.Check(worker.Card, outcome.Result);
        if (unupdated is not null)
        {
            _p.Log.Write($"slot {worker.Slot} {worker.Card}: {unupdated} (moved to its folder with the same text)");
            worker = worker with { StopWhy = unupdated };
            running[worker.Slot] = worker;
        }

        ReleaseAtRunEnd(worker, outcome.Result);   // DL-8, DL-9 (1020-B), when locks are on
        _p.Finisher.Finish(worker, outcome.Result, model, running);
        _p.Ended?.Invoke(worker.Pid);
        if (fromPage && StoppedHold is { } hold
            && tick.Cards.FirstOrDefault(c => Scheduler.Core.Contracts.Text.Same(c.Id, worker.Id)) is { } stopped)
        {
            hold.Hold(stopped);   // WT-P2
        }

        return outcome.PauseForQuota ? SchedulerState.PausedQuota : state;
    }

    /// <summary>
    /// 995-B: the claim of a queued card goes to the git writer and this returns at once, so a dispatch
    /// tick never waits for its push. The worker starts at a later tick, once <see cref="ClaimOf"/> says
    /// it landed (CM-9). False (claim in <see cref="StartWorker(int, Card, bool, string)"/>, as before)
    /// for a resume, a card not in queue/, or no writer or moves folder.
    /// </summary>
    public bool PostClaim(int slot, Card card, bool resume)
    {
        if (resume || card.Folder != CardFolder.Queue || Moves is null || Writer is null)
        {
            return false;
        }

        var claim = new PostedClaim(card);
        _claims[slot] = claim;
        PostClaimWrite(slot, claim);
        return true;
    }

    /// <summary>
    /// 995-B: how the claim posted for <paramref name="slot"/> went. A failed one is forgotten; a landed one
    /// is kept until its worker starts. One whose request is still waiting is posted again (the same request
    /// file, never a second claim), up to <see cref="ClaimTries"/> writes; then it counts as failed.
    /// </summary>
    public ClaimState ClaimOf(int slot)
    {
        if (!_claims.TryGetValue(slot, out var claim))
        {
            return ClaimState.Failed;
        }

        switch (claim.State)
        {
            case ClaimState.Landed:
                return ClaimState.Landed;
            case ClaimState.Failed:
                _claims.TryRemove(slot, out _);
                return ClaimState.Failed;
        }

        if (claim.Retry)
        {
            claim.Retry = false;
            if (claim.Tries >= ClaimTries)
            {
                _claims.TryRemove(slot, out _);
                _p.Log.Write($"slot {slot}: the claim of {claim.Card.Name} is still not on main after {claim.Tries} tries; the slot is freed");
                return ClaimState.Failed;
            }

            PostClaimWrite(slot, claim);
        }

        return ClaimState.Pending;
    }

    private void PostClaimWrite(int slot, PostedClaim claim)
    {
        var moves = Moves!;
        claim.Tries++;
        Writer!.Post($"claim {claim.Card.Name} slot {slot}", () =>
        {
            ClaimState state;
            try
            {
                claim.Request ??= moves.WriteClaim(claim.Card, slot);
                state = moves.Apply(claim.Request);
            }
            catch (Exception e)
            {
                _p.Log.Write($"slot {slot}: the claim of {claim.Card.Name} could not be written: {e.Message}");
                state = ClaimState.Pending;
            }

            if (state == ClaimState.Pending)
            {
                _p.Log.Write($"slot {slot}: the claim of {claim.Card.Name} is not on main yet; it is tried again next tick");
                claim.Retry = true;
            }
            else
            {
                claim.State = state;
            }

            return state != ClaimState.Pending;   // landed or failed: wake the tick to start the worker or free the slot
        });
    }

    /// <inheritdoc />
    public RunningWorker? StartWorker(int slot, Card card, bool resume) => StartWorker(slot, card, resume, "");

    /// <inheritdoc />
    public RunningWorker? StartWorker(int slot, Card card, bool resume, string type)
    {
        var tick = Tick ?? throw new InvalidOperationException("TickHost.Tick is not set.");
        var config = _p.Config;
        try
        {
            var (effective, typeName) = EffectiveFor(tick, slot, card, type);
            var agent = Agents.AgentOf(config, effective.Agent);
            // CM-9: the claim is on main before the worker starts, as its prompt says. 987-B: it takes its
            // turn on the git writer, so it never meets a page action or the planner step in the answers worktree.
            // 995-B: a claim posted off the tick (PostClaim) that has landed is not made again.
            var landed = _claims.TryGetValue(slot, out var posted) && posted.State == ClaimState.Landed
                && Scheduler.Core.Contracts.Text.Same(posted.Card.Name, card.Name);
            if (landed)
            {
                _claims.TryRemove(slot, out _);
            }

            if (!landed && !resume && card.Folder == CardFolder.Queue && Moves is { } moves
                && !(Writer is { } writer ? writer.Run("claim " + card.Name, () => moves.Claim(card, slot)) : moves.Claim(card, slot)))
            {
                _p.Log.Write($"slot {slot}: the claim of {card.Name} is not on main yet; it is tried again next tick");
            }

            var worktree = _p.Worktrees.Prepare(slot);
            Directory.CreateDirectory(config.LogDir);
            var start = _p.Clock.Now;
            var jobs = WorkerSelection.JobsPerWorker(_p.Cores, PoolOf(tick).MaxWorkers); // WT-11
            var files = WorkerFiles.Names(config, card, start, agent.Format);
            var locks = LocksOn && LockExe.Length > 0;
            var prompt = WorkerPrompt.Build(config, new WorkerPromptRequest
            {
                Card = card,
                Worktree = worktree,
                Slot = slot,
                Start = start,
                Jobs = jobs,
                Resume = resume,
                CategoryBrief = tick.Categories.Brief(card.Category),
                ScopeBrief = ScopeBrief.For(card, tick.Categories.Current, CategoriesLocation.Rel(config.JobsDir, ReadMain)),   // SC-4, 560-A
                Hooks = MessageHook.Delivers(agent.Format),                                               // WRK-14, 700-B
                LockExe = locks ? LockExe : "",   // dynamic locks, section 3 (1020-B)
            });
            File.WriteAllText(files.PromptFile, prompt, Utf8);

            var args = WorkerArgs.For(config, agent, jobs, effective.Model, effective.Endpoint, files.Pointer, config.WorkerWeb);
            var title = WorkerCommandLine.Title(config.Project, slot, card.Name);
            var arguments = WorkerCommandLine.Arguments(title, agent.Cli, args, files.RawLog, closeStdin: agent.Format is AgentFormat.Codex or AgentFormat.OpenCode);   // AG-17
            var commandLine = arguments.StartsWith("/c ", StringComparison.Ordinal) ? arguments[3..] : arguments;

            var env = WorkerEnv.Build(config, agent, effective, jobs, slot, null, _p.Probe, _p.Log,
                locks: locks ? new WorkerLocks(card.Name, LockExe, WorkerFiles.HooksDir(config.LogDir)) : null,   // DL-18, 1020-B
                worktree: worktree);   // AG-19
            var environment = new Dictionary<string, string?>(Scheduler.Core.Contracts.Text.Comparer);
            foreach (var name in env.Removed)
            {
                environment[name] = null;
            }

            foreach (var (name, value) in env.Set)
            {
                environment[name] = value;
            }

            if (locks)
            {
                // 1020-B: where the lock command writes its request, and the slot it names (DL-22 checks it).
                environment[Scheduler.Host.Program.LockCommand.LogDirVariable] = config.LogDir;
                environment[Scheduler.Host.Program.LockCommand.SlotVariable] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var jobName = JobObjects.Template(config.Project, "worker", slot);
            var started = _p.Starter.Start(slot, commandLine, jobName, worktree, environment);
            if (started is null)
            {
                _p.Log.Write($"slot {slot}: could not start {card.Name}");
                return null;
            }

            _p.Started?.Invoke(started.Pid);
            _p.Log.Write($"slot {slot} started: {card.Name}{(resume ? " (resume)" : "")} pid {started.Pid}");
            return new RunningWorker
            {
                Slot = slot,
                Pid = started.Pid,
                Start = started.Start,
                Card = card.Name,
                Category = card.Category,
                CardType = card.CardType,
                CardVersion = card.Version,
                Id = card.Id,
                Log = files.View,
                RawLog = files.RawLog,
                LastProgress = started.Start,
                Job = JobObjects.Name(config.Project, "worker", slot, started.Pid),
                TimeBox = card.TimeBox,
                Resume = resume,
                Msgs = true,
                Agent = effective.Agent,
                Type = typeName,
                Model = effective.Model,
                Endpoint = effective.Endpoint,
                MetricsUrl = effective.MetricsUrl,
            };
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _p.Log.Write($"slot {slot}: could not start {card.Name}: {e.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public void SaveRetries(IReadOnlyDictionary<string, int> retries) => RetriesFile.Save(_p.Files, retries);

    /// <inheritdoc />
    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state)
    {
        var tick = Tick ?? throw new InvalidOperationException("TickHost.Tick is not set.");
        var enabled = slots.ToDictionary(s => s.Slot, s => s.Enabled);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var types = PoolOf(tick).Types;
        var pool = watch.Elapsed;
        _state.Write(
            StateName(state),
            _p.Config,
            slots.Count,
            slot => enabled.GetValueOrDefault(slot, true),
            tick.Running,
            Logs.Stats,
            tick.Retries.ToDictionary(kv => kv.Key, kv => kv.Value),
            tick.Cards,
            types);
        LogSlowStatus(pool, watch.Elapsed - pool);
    }

    // 1002-B: WriteStatus was seen at 1-3 s; say which part costs it, at most every 10 minutes.
    private void LogSlowStatus(TimeSpan pool, TimeSpan write)
    {
        var now = _p.Clock.Now;
        if (pool + write < TimeSpan.FromSeconds(1) || (_statusLogged is { } last && now - last < TimeSpan.FromMinutes(10)))
        {
            return;
        }

        _statusLogged = now;
        _p.Log.Write($"WriteStatus took {(pool + write).TotalSeconds:0.00}s: worker pool {pool.TotalSeconds:0.00}s, "
            + $"status, running and retries files {write.TotalSeconds:0.00}s ({Tick?.Cards.Count ?? 0} cards, {Tick?.Running.Count ?? 0} running).");
    }

    /// <summary>The state as status.txt and the page spell it (PAGE-8).</summary>
    // The pool as the tick dispatches it: types made from the slots are rebuilt from their current
    // state each time (WT-C10), a pool form is read as saved. In a pool (MP-C4) the pool file's types,
    // which the tick was given, so a worker starts with the type the pool's dispatch chose (998-B).
    // The tick's own Types is that rule, from the same config the tick was built with.
    internal static WorkerPool PoolOf(Scheduler.Core.Tick.SchedulerTick tick, SchedulerConfig config) =>
        tick.Types;

    private WorkerPool PoolOf(Scheduler.Core.Tick.SchedulerTick tick) => PoolOf(tick, _p.Config);

    /// <summary>WT-14: the type a worker started in <paramref name="slot"/> for <paramref name="card"/> would be.</summary>
    public string TypeFor(int slot, Card card) =>
        Tick is { } tick ? EffectiveFor(tick, slot, card, "").Type : "";

    // WT-W2/WT-3: the settings a worker starts with. The type the dispatcher chose (WT-5) is the answer
    // when it gave one (959-A): a folder is only a folder (WT-W1), so its number says nothing about who
    // runs in it. With no type to go on - the dry run's line, a host that starts by folder - a type made
    // from slots starts with its slot's own settings (the same thing), and in the pool form the type is
    // the first one in config order that fits the card and has room, as the dispatcher would have chosen it.
    private (EffectiveWorker Effective, string Type) EffectiveFor(Scheduler.Core.Tick.SchedulerTick tick, int slot, Card card, string type)
    {
        var pool = PoolOf(tick);
        if (type.Length > 0 && pool.Types.FirstOrDefault(t => Text.Same(t.Name, type)) is { } chosen)
        {
            return (WorkerSelection.Effective(_p.Config, chosen), chosen.Name);
        }

        if (pool.Migrated)
        {
            return (WorkerSelection.Effective(_p.Config, tick.Workers.Workers, slot), Scheduler.Core.Launch.Adoption.TypeOfSlot(pool, slot));
        }

        var models = ReadModels();
        foreach (var candidate in pool.Types)
        {
            var e = WorkerSelection.Effective(_p.Config, candidate);
            var dispatch = new DispatchType
            {
                Name = candidate.Name,
                Max = candidate.Max,
                Running = tick.Running.Values.Count(w => Scheduler.Core.Contracts.Text.Same(w.Type, candidate.Name)),
                Model = e.Model,
                ModelNames = models.GetValueOrDefault(e.Model) ?? [],
                IsDefault = pool.IsDefault(candidate.Name),
                Endpoint = e.Endpoint,
                Agent = candidate.Agent,
                Slots = candidate.Slots,
            };
            if (dispatch.Running < dispatch.Max && TypeDispatcher.Fits(dispatch, card, false))
            {
                return (e, candidate.Name);
            }
        }

        return (WorkerSelection.Effective(_p.Config, pool.Types[0]), pool.Types[0].Name);
    }

    public static string StateName(SchedulerState state) => state switch
    {
        SchedulerState.Running => "running",
        SchedulerState.Paused => "paused",
        SchedulerState.PausedRed => "paused-red",
        SchedulerState.PausedQuota => "paused-quota",
        _ => "draining",
    };

    // The worker's log format: its agent's, and plain text when its raw log is its readable log.
    private AgentFormat FormatOf(RunningWorker worker)
    {
        if (worker.Log.Length == 0 || worker.Log == worker.RawLog)
        {
            return AgentFormat.Text;
        }

        var format = Agents.AgentOf(_p.Config, worker.Agent).Format;
        return format == AgentFormat.Text ? AgentFormat.Copilot : format;
    }

    private static IReadOnlyList<string>? ReadLines(string path)
    {
        try
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8);
            return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteFlag(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text, Utf8);
        }
        catch (IOException)
        {
            // The flag is written again at the next watch only if the condition still holds.
        }
    }
}

/// <summary>995-B: a claim handed to the git writer; written on the writer's thread, read on the tick's.</summary>
internal sealed class PostedClaim(Card card)
{
    private volatile int _state = (int)ClaimState.Pending;
    private volatile bool _retry;

    /// <summary>The card claimed.</summary>
    public Card Card { get; } = card;

    /// <summary>The request file, once written; the same file is applied again on a retry.</summary>
    public string? Request { get; set; }

    /// <summary>Pending until the writer knows.</summary>
    public ClaimState State { get => (ClaimState)_state; set => _state = (int)value; }

    /// <summary>The write ended with the request still waiting: post it again.</summary>
    public bool Retry { get => _retry; set => _retry = value; }

    /// <summary>Writes posted so far.</summary>
    public int Tries { get; set; }
}
