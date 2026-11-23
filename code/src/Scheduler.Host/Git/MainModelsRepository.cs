using System.Diagnostics;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Git;

/// <summary>
/// 1062-B: one repository's model of main in the Host (state-cache.md section 6, "Git/MainModels"), shared by every
/// project whose jobs folder is in it (DL-22). It loads in two phases in the background (MM-5: open cards, requests
/// and settings, then the done and archive headers), reads <c>&lt;remote&gt;/&lt;branch&gt;</c> from the refs files on
/// every <see cref="Tick"/> and advances when it moved (MM-7, MM-8, MM-10), runs the fetch the gate allows
/// (<see cref="Fetch"/>, MM-9), and reads through the git CLI when the in-process reader refuses the repository
/// or <c>gitReader: "cli"</c> asks for it (MM-2, MM-16), or after three failed reads in a row until restart
/// (section 4). It logs one startup line per load. 1063-B: the tick reads it (<see cref="IMainFeed"/>).
/// </summary>
public sealed class MainModelsRepository : IDisposable, IMainFeed
{
    private static long s_advanceTicks;

    /// <summary>MM-18: the time every repository's advances took since the process started, in milliseconds.</summary>
    public static double AdvanceMsTotal => TimeSpan.FromTicks(Interlocked.Read(ref s_advanceTicks)).TotalMilliseconds;

    /// <summary>Failed reads in a row after which a repository is read through the CLI until restart (section 4).</summary>
    public const int FailuresBeforeCli = 3;

    private readonly Lock _gate = new();
    private readonly GitRunner _git;
    private readonly ISchedulerLog _log;
    private readonly IClock _clock;
    private readonly Func<IMainModelSource> _cli;
    private readonly Dictionary<string, int> _jobsDirs = new(StringComparer.Ordinal);
    private IMainModelSource _source;
    private RepositoryModel? _model;
    private Task _loading = Task.CompletedTask;
    private readonly List<IMainModelSource> _retired = [];
    private bool _reload;
    private bool _switched;
    private bool _noRefLogged;
    private int _failures;   // failures outside the model's own steps (the ref read, a load), in a row

    /// <summary>A repository's model; <see cref="MainModels.Join"/> makes them.</summary>
    /// <param name="repository">The repository's full path.</param>
    /// <param name="remote">The remote.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="poll">The poll interval: a fetch is shared for this long (MM-9).</param>
    /// <param name="forceCli">The <c>gitReader: "cli"</c> setting.</param>
    /// <param name="git">The runner for the fetch and the CLI reader.</param>
    /// <param name="log">The log for the startup lines.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="loads">Shared by every repository: at most this many load at once (MM-5).</param>
    /// <param name="inProcess">A test's in-process source; null opens <see cref="MainObjects"/>.</param>
    /// <param name="cli">A test's CLI source; null makes <see cref="CliObjectSource"/>.</param>
    public MainModelsRepository(
        string repository,
        string remote,
        string branch,
        TimeSpan poll,
        bool forceCli,
        GitRunner git,
        ISchedulerLog log,
        IClock clock,
        SemaphoreSlim loads,
        IMainModelSource? inProcess = null,
        Func<string, IMainModelSource>? cli = null)
    {
        Repository = repository;
        Remote = remote;
        Branch = branch;
        _git = git;
        _log = log;
        _clock = clock;
        Loads = loads;
        string? refusal = forceCli ? "gitReader: cli" : null;
        IMainModelSource? source = inProcess;
        if (source is null && refusal is null)
        {
            var objects = MainObjects.TryOpen(repository, out refusal);
            source = objects is null ? null : new MainObjectsSource(objects);
        }

        var cliFor = cli ?? (why => new CliObjectSource(git, repository, why));
        _source = source is not null && !forceCli ? source
            : refusal is AbsentSource.NotARepository or AbsentSource.NoGitFolder ? new AbsentSource(refusal)   // git could not read it either
            : cliFor(refusal ?? "not readable");
        _cli = () => cliFor("in-process reads failed");
        Gate = new FetchGate(clock, poll, RunFetch);
    }

    /// <summary>The repository's full path: the key of <see cref="MainModels"/>.</summary>
    public string Repository { get; }

    /// <summary>The remote whose branch the model follows.</summary>
    public string Remote { get; }

    /// <summary>The branch.</summary>
    public string Branch { get; }

    /// <summary>The tracking ref read every tick (MM-8).</summary>
    public string TrackingRef => $"refs/remotes/{Remote}/{Branch}";

    /// <summary>"in process" or "git CLI (&lt;reason&gt;)".</summary>
    public string Reader
    {
        get
        {
            lock (_gate)
            {
                return _source.Kind;
            }
        }
    }

    /// <summary>The model, or null before the first load has read the ref.</summary>
    public RepositoryModel? Model => Volatile.Read(ref _model);

    /// <inheritdoc />
    IMainModel? IMainFeed.Model => Model;

    /// <summary>MM-9: when the fetch runs; every consumer asks it instead of fetching for itself.</summary>
    public FetchGate Gate { get; }

    /// <summary>The jobs folders of the projects that joined, in order.</summary>
    public IReadOnlyList<string> JobsDirs
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobsDirs.Keys.Order(StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>The load in flight or the last one (both phases); a test waits on it.</summary>
    public Task Loading
    {
        get
        {
            lock (_gate)
            {
                return _loading;
            }
        }
    }

    /// <summary>Phase 1's time of the last load, in milliseconds.</summary>
    public long PhaseOneMs { get; private set; }

    /// <summary>Phase 2's time of the last load, in milliseconds.</summary>
    public long PhaseTwoMs { get; private set; }

    /// <summary>The last advance's time (MM-18's <c>model &lt;ms&gt;</c>), in milliseconds.</summary>
    public double AdvanceMs { get; private set; }

    internal SemaphoreSlim Loads { get; }

    /// <summary>
    /// MM-8, MM-10: reads the tracking ref from the refs files; when it differs from the snapshot's commit, the
    /// model advances in process. True when the model moved. Never throws: a failure keeps the snapshot (CARD-3),
    /// and after <see cref="FailuresBeforeCli"/> in a row the repository switches to the CLI reader.
    /// </summary>
    public bool Tick()
    {
        IMainModelSource source;
        RepositoryModel? model;
        lock (_gate)
        {
            source = _source;
            model = _model;
        }

        if (source is AbsentSource)
        {
            return false;
        }

        if (model is null)
        {
            // Not loaded yet (no ref at start, or a failed load): try again, unless a load is in flight.
            if (Loading.IsCompleted)
            {
                Load();
            }

            return false;
        }

        var started = Stopwatch.GetTimestamp();
        bool moved;
        try
        {
            using var session = source.Session();
            var sha = source.ReadRef(TrackingRef);
            moved = model.RefRead(sha);
            _failures = 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // the ref files could not be read: as a failed step
            Failed(source, e.Message);
            return false;
        }

        if (moved)
        {
            var took = Stopwatch.GetElapsedTime(started);
            AdvanceMs = took.TotalMilliseconds;
            _ = Interlocked.Add(ref s_advanceTicks, took.Ticks);
        }

        CheckFailures(source, model);
        return moved;
    }

    /// <summary>MM-9: the gate's answer for <paramref name="why"/>; a fetch that ran is followed by the ref read (MM-8).</summary>
    public FetchAnswer Fetch(FetchAsk why)
    {
        var answer = Gate.Ask(why);
        if (answer == FetchAnswer.Fetched)
        {
            Tick();
        }

        return answer;
    }

    /// <summary>
    /// Starts a load in the background (both phases, MM-5), or marks the one in flight to run again (a project
    /// joined with another jobs folder, or the reader changed). The task is <see cref="Loading"/>.
    /// </summary>
    public Task Load()
    {
        lock (_gate)
        {
            _reload = true;
            if (_loading.IsCompleted)
            {
                _loading = Background(LoadLoop);
            }

            return _loading;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _source.Dispose();
            foreach (var old in _retired)
            {
                old.Dispose();
            }

            _retired.Clear();
        }
    }

    /// <summary>A project joined with <paramref name="jobsDir"/>: true when it is new to the model (a reload is due).</summary>
    public bool AddJobsDir(string jobsDir)
    {
        lock (_gate)
        {
            _jobsDirs[jobsDir] = _jobsDirs.GetValueOrDefault(jobsDir) + 1;
            return _jobsDirs[jobsDir] == 1;
        }
    }

    /// <summary>A project with <paramref name="jobsDir"/> left: true when no project is left.</summary>
    public bool RemoveJobsDir(string jobsDir)
    {
        lock (_gate)
        {
            if (_jobsDirs.TryGetValue(jobsDir, out var n))
            {
                if (n <= 1)
                {
                    _jobsDirs.Remove(jobsDir);
                }
                else
                {
                    _jobsDirs[jobsDir] = n - 1;
                }
            }

            return _jobsDirs.Count == 0;
        }
    }

    private bool RunFetch() => _git.Run(Repository, "fetch", "-q", Remote).Ok;

    // The load runs on its own thread below normal priority: it gives way to the tick, the page and the other
    // projects' joins (startup gate), and never holds a thread-pool thread.
    private static Task Background(Action run)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                run();
                done.SetResult();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "main model load",
        };
        thread.Start();
        return done.Task;
    }

    private void LoadLoop()
    {
        while (true)
        {
            IMainModelSource source;
            MainLayout layout;
            lock (_gate)
            {
                if (!_reload)
                {
                    return;
                }

                _reload = false;
                source = _source;
                layout = new MainLayout(_jobsDirs.Keys);
            }

            if (layout.JobsDirs.Count == 0)
            {
                continue;
            }

            RepositoryModel? model = null;
            Loads.Wait();
            try
            {
                model = PhaseOne(source, layout);
            }
            finally
            {
                Loads.Release();
            }

            if (model is null)
            {
                continue;
            }

            Loads.Wait();
            try
            {
                PhaseTwo(source, model);
            }
            finally
            {
                Loads.Release();
            }
        }
    }

    // MM-5 phase 1: the ref, then the jobs subtrees and settings files; the model is published when it is done.
    private RepositoryModel? PhaseOne(IMainModelSource source, MainLayout layout)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var model = new RepositoryModel(source, layout, _clock);
            using (source.Session())
            {
                if (source.ReadRef(TrackingRef) is not { } sha)
                {
                    if (!_noRefLogged)
                    {
                        _noRefLogged = true;
                        _log.Write($"main model: {Repository}: {source.Kind}, no {Remote}/{Branch} yet");
                    }

                    return null;
                }

                if (!model.Load(sha))
                {
                    _log.Write($"main model: {Repository}: {source.Kind}, load failed: {model.LastError}");
                    Failed(source, model.LastError ?? "load failed");
                    return null;
                }
            }

            PhaseOneMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _failures = 0;
            lock (_gate)
            {
                if (!ReferenceEquals(source, _source))
                {
                    return null;   // switched to the CLI meanwhile: that load follows
                }

                Volatile.Write(ref _model, model);
            }

            var open = layout.JobsDirs.Sum(d => model.Cards(d).Count);
            _log.Write($"main model: {Repository}: {source.Kind}, {open} open card(s) at {Short(model.Commit)} in {PhaseOneMs} ms");
            return model;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Write($"main model: {Repository}: {source.Kind}, load failed: {e.Message}");
            Failed(source, e.Message);
            return null;
        }
    }

    // MM-5 phase 2: the done and archive headers, in the background.
    private void PhaseTwo(IMainModelSource source, RepositoryModel model)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            bool ok;
            using (source.Session())
            {
                ok = model.LoadDone();
            }

            PhaseTwoMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (ok || model.DoneLoaded)
            {
                var done = model.Snapshot?.Layout.JobsDirs.Sum(d => model.DoneCards(d).Count) ?? 0;
                _log.Write($"main model: {Repository}: {done} done card(s) in {PhaseTwoMs} ms");
            }
            else
            {
                _log.Write($"main model: {Repository}: done cards not read: {model.LastError}");
            }

            CheckFailures(source, model);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Write($"main model: {Repository}: done cards not read: {e.Message}");
        }
    }

    private void CheckFailures(IMainModelSource source, RepositoryModel model)
    {
        if (model.Failures >= FailuresBeforeCli)
        {
            SwitchToCli(source, model.LastError ?? "read failed");
        }
    }

    // A failure outside the model's own steps (the ref read, a load): counted the same way.
    private void Failed(IMainModelSource source, string why)
    {
        if (Interlocked.Increment(ref _failures) >= FailuresBeforeCli)
        {
            SwitchToCli(source, why);
        }
    }

    // Section 4: three failures in a row log once and switch the repository to the CLI reader until restart.
    private void SwitchToCli(IMainModelSource from, string why)
    {
        lock (_gate)
        {
            if (from.IsCli || _switched || !ReferenceEquals(from, _source))
            {
                return;
            }

            _switched = true;
            _retired.Add(from);   // another thread may still be reading through it: disposed with this
            _source = _cli();
            _failures = 0;
            Volatile.Write(ref _model, null);
        }
        _log.Write($"main model: {Repository}: {why}; {_source.Kind} until restart");
        Load();
    }

    private static string Short(string? sha) => sha is null ? "-" : sha[..Math.Min(7, sha.Length)];
}
