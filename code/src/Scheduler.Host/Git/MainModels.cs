using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Git;

/// <summary>What a project hands <see cref="MainModels.Join"/>: its repository, branch and jobs folder.</summary>
/// <param name="Repository">The repository's folder.</param>
/// <param name="Remote">The remote.</param>
/// <param name="Branch">The branch.</param>
/// <param name="JobsDir">The project's jobs folder, relative to the repository.</param>
/// <param name="Poll">The poll interval (MM-9).</param>
/// <param name="ForceCli">The <c>gitReader: "cli"</c> setting (MM-2).</param>
/// <param name="Git">The runner for the fetch and the CLI reader.</param>
/// <param name="Log">The log for the startup lines.</param>
/// <param name="Clock">The clock.</param>
/// <param name="LoadNow">Start the load at once; false leaves it to the first <see cref="MainModelsRepository.Tick"/>, off the page's start (startup gate).</param>
public sealed record MainModelsJoin(
    string Repository,
    string Remote,
    string Branch,
    string JobsDir,
    TimeSpan Poll,
    bool ForceCli,
    GitRunner Git,
    ISchedulerLog Log,
    IClock Clock,
    bool LoadNow = true);

/// <summary>
/// 1062-B (state-cache.md section 6, "Git/MainModels"): the Host's models of main, one per repository, keyed by the
/// repository's full path as the lock table is (DL-22): projects sharing a repository share its model, whose
/// layout holds every one's jobs folder. A project joins when its parts are made and leaves when they are disposed;
/// repositories load in parallel, at most <c>PTK_JOBS</c> at once (MM-5). <see cref="Shared"/> is the process's one
/// set, so a pool's projects share it without being told; a test makes its own.
/// </summary>
public sealed class MainModels
{
    private readonly Dictionary<string, MainModelsRepository> _repos = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loads;

    /// <param name="parallel">At most this many repositories load at once.</param>
    public MainModels(int parallel) => _loads = new SemaphoreSlim(Math.Max(1, parallel));

    /// <summary>The process's models; at most <c>PTK_JOBS</c> (else 5) load at once.</summary>
    public static MainModels Shared { get; } = new(Parallel(Environment.GetEnvironmentVariable("PTK_JOBS")));

    /// <summary>The repositories with a model, by full path.</summary>
    public IReadOnlyList<MainModelsRepository> Repositories
    {
        get
        {
            lock (_repos)
            {
                return [.. _repos.Values];
            }
        }
    }

    /// <summary>The load limit for <paramref name="jobs"/> (the <c>PTK_JOBS</c> value): a positive number, else 5.</summary>
    public static int Parallel(string? jobs) =>
        int.TryParse(jobs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 5;

    /// <summary>The repository's full path, the key: absolute, separators as the PC writes them, no trailing one.</summary>
    public static string Key(string repository) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));

    /// <summary>The model of <paramref name="repository"/>, or null when no project has joined it.</summary>
    public MainModelsRepository? For(string repository)
    {
        lock (_repos)
        {
            return _repos.GetValueOrDefault(Key(repository));
        }
    }

    /// <summary>
    /// A project joins its repository's model, made (and its load started) when it is the first; a jobs folder new
    /// to the model starts a reload with it. The first project's remote, branch, poll and reader setting are the
    /// model's.
    /// </summary>
    public MainModelsRepository Join(MainModelsJoin join)
    {
        var key = Key(join.Repository);
        var jobsDir = join.JobsDir.Replace('\\', '/').Trim('/');
        MainModelsRepository repo;
        lock (_repos)
        {
            if (!_repos.TryGetValue(key, out var known))
            {
                known = new MainModelsRepository(key, join.Remote, join.Branch, join.Poll, join.ForceCli,
                    join.Git, join.Log, join.Clock, _loads);
                _repos[key] = known;
            }

            repo = known;
        }

        if (repo.AddJobsDir(jobsDir) && (join.LoadNow || repo.Model is not null || !repo.Loading.IsCompleted))
        {
            repo.Load();
        }

        return repo;
    }

    /// <summary>A project's parts are disposed: its jobs folder leaves; the last to leave drops the model.</summary>
    public void Leave(MainModelsRepository repo, string jobsDir)
    {
        lock (_repos)
        {
            if (!repo.RemoveJobsDir(jobsDir.Replace('\\', '/').Trim('/')))
            {
                return;
            }

            if (_repos.TryGetValue(repo.Repository, out var known) && ReferenceEquals(known, repo))
            {
                _repos.Remove(repo.Repository);
            }
        }

        repo.Dispose();
    }
}
