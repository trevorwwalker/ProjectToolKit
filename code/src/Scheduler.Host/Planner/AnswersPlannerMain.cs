using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Planner;
using Scheduler.Host.Git;

namespace Scheduler.Host.Planner;

/// <summary>
/// <see cref="IPlannerMain"/> over the answers worktree after it was reset to the remote's main
/// (PLAN-4, PLAN-5): the files under the jobs folder as git lists them, read from disk and from blobs.
/// 1064-B (state-cache.md MM-14): or over one snapshot of the repository's model of main
/// (<see cref="AnswersPlannerMain(IMainModel, MainSnapshot, GitRunner, string, string)"/>): the jobs tree and its
/// blob ids from the snapshot, a file's text read in process, with no reset of the answers worktree and no git
/// process. Only a blob the snapshot does not name (the planner's base, an older copy) is read through the CLI.
/// </summary>
public sealed class AnswersPlannerMain : IPlannerMain
{
    private readonly GitRunner _git;
    private readonly string _dir;
    private readonly string _jobsDir;
    private readonly IMainModel? _model;
    private readonly MainSnapshot? _at;

    public AnswersPlannerMain(GitRunner git, string answersDir, string jobsDir)
    {
        _git = git;
        _dir = answersDir;
        _jobsDir = jobsDir.TrimEnd('/');
    }

    /// <summary>Main as <paramref name="at"/> of <paramref name="model"/> has it; <paramref name="repo"/> is where the CLI reads what it does not hold.</summary>
    public AnswersPlannerMain(IMainModel model, MainSnapshot at, GitRunner git, string repo, string jobsDir)
        : this(git, repo, jobsDir)
    {
        _model = model;
        _at = at;
    }

    /// <summary>The commit this reads (the snapshot's), or null for the answers worktree's HEAD.</summary>
    public string? Commit => _at?.Commit;

    /// <inheritdoc />
    public IReadOnlyList<string> Paths()
    {
        if (_at is not null)
        {
            return [.. _at.Entries(_jobsDir).Select(e => e.Path)];
        }

        var r = _git.Run(_dir, "-c", "core.quotepath=off", "ls-tree", "-r", "--name-only", "HEAD", "--", _jobsDir + "/");
        return r.Ok ? [.. r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [];
    }

    /// <inheritdoc />
    public string? BlobId(string path)
    {
        if (_at is not null && InJobs(path))
        {
            return _at.Files.GetValueOrDefault(path.Trim('/'));
        }

        var r = _git.Run(_dir, "rev-parse", "--verify", "-q", (_at?.Commit ?? "HEAD") + ":" + path);
        return r.Ok ? r.Stdout.Trim() : null;
    }

    /// <inheritdoc />
    public string? ReadFile(string path)
    {
        if (_at is not null)
        {
            // The model's text is at its current commit: read in process while that is still the snapshot planned on.
            if (ReferenceEquals(_model!.Snapshot, _at))
            {
                return _model.Text(path);
            }

            var show = _git.Run(_dir, "show", _at.Commit + ":" + path);
            return show.Ok ? show.Stdout : null;
        }

        var f = Path.Combine(_dir, path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(f) ? File.ReadAllText(f) : null;
    }

    /// <inheritdoc />
    public string? ReadBlob(string blobId)
    {
        if (_at is not null && ReferenceEquals(_model!.Snapshot, _at)
            && _at.Files.FirstOrDefault(f => string.Equals(f.Value, blobId, StringComparison.OrdinalIgnoreCase)).Key is { } path)
        {
            return _model.Text(path);
        }

        var r = _git.Run(_dir, "cat-file", "-p", blobId);
        return r.Ok ? r.Stdout : null;
    }

    private bool InJobs(string path) => path.Trim('/').StartsWith(_jobsDir + "/", StringComparison.Ordinal);
}
