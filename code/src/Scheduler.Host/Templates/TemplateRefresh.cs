// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Templates;

/// <summary>A generic file whose copy was edited in the project: to be shown under Waiting on you with Overwrite and Keep.</summary>
/// <param name="Target">The copy's path in the project.</param>
/// <param name="Template">The template's path under <c>assets/templates/</c>.</param>
public sealed record TemplateEdit(string Target, string Template);

/// <summary>
/// Keeps a project's generic files in step with ProjectToolkit's templates (roadmap item 1, card
/// 540-B). At start, and each time the toolkit's <c>origin/main</c> moves, each copy on the
/// project's main is compared with the template on the toolkit's main. A copy that differs and is
/// a version the project took unedited (a past version of the template in the toolkit's history, or
/// the one the scheduler last wrote) is replaced, all in one commit through the answers worktree.
/// A copy that is neither was edited in the project: it is listed in <see cref="Edited"/>, not
/// overwritten, until the developer chooses <see cref="Overwrite"/> or <see cref="Keep"/> (which
/// holds until the template changes again). Only <see cref="GenericFiles"/> are ever written.
/// </summary>
public sealed class TemplateRefresh
{
    private readonly GitRunner _git;
    private readonly string _toolkitDir;
    private readonly MainRepository _toolkit;
    private readonly IMainRepository _project;
    private readonly IJobsCommitter _committer;
    private readonly string _stateFile;
    private readonly ISchedulerLog? _log;
    private readonly string _toolkitRef;
    private readonly string _remote;
    private readonly string _branch;
    private readonly Dictionary<string, string> _blobHashes = new(StringComparer.Ordinal);
    private readonly Lazy<MainObjects?> _objects;
    private readonly Lock _walkLock = new();
    private readonly HashSet<ObjectId> _walked = [];
    private readonly HashSet<ObjectId> _templateTrees = [];
    private readonly Dictionary<string, HashSet<string>> _versions = new(StringComparer.Ordinal);
    private bool _merges;
    private readonly Lock _lock = new();
    private string? _checked;
    private bool _toldNoSha;
    private readonly bool _noGit;
    // 1118-B: the last refresh that failed, so a failure is logged once and not tried again every tick.
    private string? _failedError;
    private DateTimeOffset _failedAt;
    private string? _failedToolkit;
    private string? _failedMain;
    // 1116-B: replaced whole (never changed in place) under _lock, and read without it: see Edited.
    private volatile IReadOnlyList<TemplateEdit> _edited = [];

    /// <param name="git">Runs git.</param>
    /// <param name="toolkitDir">ProjectToolkit's checkout (<see cref="ToolkitLocator"/>).</param>
    /// <param name="project">The project's main, read as the scheduler reads it.</param>
    /// <param name="committer">Commits to the project's main through the answers worktree.</param>
    /// <param name="jobsDir">The project's queue folder: <c>jobs</c>, or <c>docs/jobs</c>.</param>
    /// <param name="stateFile">The <c>templates.json</c> state file.</param>
    /// <param name="log">The scheduler log, or null.</param>
    /// <param name="remote">The toolkit's remote, <c>origin</c>.</param>
    /// <param name="branch">The toolkit's main branch, <c>main</c>.</param>
    public TemplateRefresh(GitRunner git, string toolkitDir, IMainRepository project, IJobsCommitter committer,
        string jobsDir, string stateFile, ISchedulerLog? log = null, string remote = "origin", string branch = "main")
    {
        _git = git;
        _toolkitDir = toolkitDir;
        _toolkit = new MainRepository(git, toolkitDir, remote, branch);
        _toolkitRef = remote + "/" + branch;
        _remote = remote;
        _branch = branch;
        _objects = new Lazy<MainObjects?>(() => InProcessHistory.Open(toolkitDir));
        // INS-17: a kit folder with no .git (an installed copy) is read from disk and never given to git.
        _noGit = !Directory.Exists(Path.Combine(toolkitDir, ".git")) && !File.Exists(Path.Combine(toolkitDir, ".git"));
        _project = project;
        _committer = committer;
        _stateFile = stateFile;
        _log = log;
        Files = GenericFiles.For(jobsDir);
    }

    /// <summary>How long a failed refresh waits before it is tried again with nothing changed (1118-B).</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>The time, replaced by a test.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>1118-B: true while the project is paused; a paused project neither fetches nor refreshes templates.</summary>
    public Func<bool>? Paused { get; set; }

    /// <summary>The generic files of this project.</summary>
    public IReadOnlyList<GenericFile> Files { get; }

    /// <summary>The copies edited in this project, as the last finished check found them.</summary>
    /// <remarks>
    /// 1116-B (MM-15): read without the lock. A check holds the lock through its fetch, commit and reset on the git
    /// writer's thread (987-B), and the page's refresh reads this on the tick's thread: on the live pool
    /// (2026-11-22) a refresh commit that failed every tick made every PageRefresh wait 5-10 s for it.
    /// </remarks>
    public IReadOnlyList<TemplateEdit> Edited => _edited;

    /// <summary>How many checks have run (each compares every copy with its template).</summary>
    public int Checks { get; private set; }

    /// <summary>
    /// Once a tick: fetch the toolkit and, at start or when its main has moved since the last
    /// finished check, check. True when a refresh commit was pushed.
    /// </summary>
    /// <param name="ask">Who asks for the toolkit's fetch (MM-9): a click or a move request never fetches when the toolkit's repository has a model.</param>
    public bool Step(FetchAsk ask = FetchAsk.Poll)
    {
        if (Paused?.Invoke() == true)
        {
            return false;
        }

        if (!_noGit)
        {
            FetchToolkit(ask);
        }

        var sha = ToolkitHead();
        if (sha is null)
        {
            if (!_toldNoSha)
            {
                _log?.Write(_noGit
                    ? $"templates: no readable {TemplateHistory.RelativePath} in the toolkit at {_toolkitDir}; the generic files are not kept in step"
                    : $"templates: no {_toolkitRef} in the toolkit at {_toolkitDir}; the generic files are not kept in step");
                _toldNoSha = true;
            }

            return false;
        }

        if (sha == _checked)
        {
            return false;
        }

        // 1118-B: a failed refresh is tried again after an hour, or when the toolkit's or the project's main moved.
        if (_failedError is not null && sha == _failedToolkit && Now() - _failedAt < RetryAfter
            && _project.HeadSha() == _failedMain)
        {
            return false;
        }

        var r = Check();
        if (r.Ok)
        {
            _checked = sha;
        }

        return r.Ok && r.Sha.Length > 0;
    }

    // MM-9: when the toolkit's repository has a model in this process (it is a project of the pool), the fetch is
    // that model's gate's - one per repository per poll, shared with the project's own tick - and a tick that does
    // not fetch reads the tracking ref in process (MM-8). A toolkit outside the pool is fetched here as before,
    // but not for a click or a move request (1002-B: they never fetch).
    private void FetchToolkit(FetchAsk ask)
    {
        if (IMainFeed.Shared(_toolkitDir, _remote, _branch) is { Model: not null } feed)
        {
            if (feed.Fetch(ask) != FetchAnswer.Fetched)
            {
                _ = feed.Tick();
            }

            return;
        }

        if (ask is not (FetchAsk.Click or FetchAsk.MoveRequest))
        {
            _toolkit.Fetch();
        }
    }

    /// <summary>
    /// Compare every copy on the project's main with the toolkit's template and refresh the unedited
    /// ones in one commit. Ok with the commit's sha, Ok with no sha when nothing needed writing, or
    /// the commit's error (the next step tries again).
    /// </summary>
    public CommitResult Check()
    {
        lock (_lock)
        {
            Checks++;
            var sha = ToolkitHead() ?? "";
            _project.Fetch();
            var state = TemplateState.Load(_stateFile);
            var writes = new Dictionary<string, string>(StringComparer.Ordinal);
            var edited = new List<TemplateEdit>();
            foreach (var f in Files)
            {
                var template = ToolkitFile($"{GenericFiles.TemplatesDir}/{f.Template}");
                if (template is null)
                {
                    continue; // not a template of this toolkit's version
                }

                var current = TemplateState.Hash(template);
                var s = state.Of(f.Target);
                var copy = _project.ReadFile(f.Target);
                var hash = copy is null ? null : TemplateState.Hash(copy);
                if (hash == current)
                {
                    s.Taken = current;
                    s.Kept = "";
                }
                else if (hash is null || hash == s.Taken || IsPastVersion(f.Template, copy!))
                {
                    writes[f.Target] = TemplateState.Normalize(template);
                }
                else if (s.Kept != current)
                {
                    edited.Add(new TemplateEdit(f.Target, f.Template));
                }
            }

            var result = new CommitResult { Ok = true };
            if (writes.Count > 0)
            {
                result = _committer.Apply(new JobsChange
                {
                    Writes = writes,
                    Message = $"jobs: refresh the generic files from the toolkit ({Short(sha)})",
                });
                if (result.Ok)
                {
                    foreach (var (target, text) in writes)
                    {
                        var s = state.Of(target);
                        s.Taken = TemplateState.Hash(text);
                        s.Kept = "";
                    }

                    _failedError = null;
                    _log?.Write($"templates: refreshed {string.Join(", ", writes.Keys)} from the toolkit ({Short(sha)})");
                }
                else
                {
                    if (result.Error != _failedError)
                    {
                        _log?.Write($"templates: could not refresh {string.Join(", ", writes.Keys)}: {result.Error}");
                    }

                    _failedError = result.Error ?? "";
                    _failedAt = Now();
                    _failedToolkit = sha.Length > 0 ? sha : null;
                    _failedMain = _project.HeadSha();
                }
            }

            foreach (var e in edited.Where(e => !_edited.Contains(e)))
            {
                _log?.Write($"templates: {e.Target} was edited in this project; it differs from the toolkit's {GenericFiles.TemplatesDir}/{e.Template} and waits for Overwrite or Keep");
            }

            _edited = edited;
            state.Save(_stateFile);
            return result;
        }
    }

    /// <summary>Overwrite: the edited copy of <paramref name="target"/> takes the toolkit's template, in one commit.</summary>
    public CommitResult Overwrite(string target)
    {
        lock (_lock)
        {
            var f = Files.FirstOrDefault(f => f.Target == target);
            var template = f is null ? null : ToolkitFile($"{GenericFiles.TemplatesDir}/{f.Template}");
            if (f is null || template is null)
            {
                return new CommitResult { Ok = false, Error = $"{target} is not a generic file of this project" };
            }

            var text = TemplateState.Normalize(template);
            var sha = ToolkitHead() ?? "";
            var r = _committer.Apply(new JobsChange
            {
                Writes = new Dictionary<string, string>(StringComparer.Ordinal) { [target] = text },
                Message = $"jobs: take the toolkit's {target} ({Short(sha)})",
            });
            if (r.Ok)
            {
                var state = TemplateState.Load(_stateFile);
                var s = state.Of(target);
                s.Taken = TemplateState.Hash(text);
                s.Kept = "";
                state.Save(_stateFile);
                _edited = [.. _edited.Where(e => e.Target != target)];
                _log?.Write($"templates: {target} overwritten with the toolkit's template ({Short(sha)})");
            }

            return r;
        }
    }

    /// <summary>Keep: leave the project's copy of <paramref name="target"/> and stop asking until the template changes again. False when it is not a generic file.</summary>
    public bool Keep(string target)
    {
        lock (_lock)
        {
            var f = Files.FirstOrDefault(f => f.Target == target);
            var template = f is null ? null : ToolkitFile($"{GenericFiles.TemplatesDir}/{f.Template}");
            if (f is null || template is null)
            {
                return false;
            }

            var state = TemplateState.Load(_stateFile);
            state.Of(target).Kept = TemplateState.Hash(template);
            state.Save(_stateFile);
            _edited = [.. _edited.Where(e => e.Target != target)];
            _log?.Write($"templates: {target} kept as the project has it, until the toolkit's template changes");
            return true;
        }
    }

    /// <summary>
    /// 1065-B (MM-14): the toolkit's <c>origin/main</c> read in process (a ref file), else <c>rev-parse</c>.
    /// </summary>
    public string? ToolkitHead()
    {
        if (_noGit)
        {
            return TemplateHistory.Load(_toolkitDir)?.Version; // INS-17: the manifest's version stands for the kit's main
        }

        if (_objects.Value is { } objects)
        {
            try
            {
                return objects.Resolve(_toolkitRef)?.ToString();
            }
            catch (Exception e) when (InProcessHistory.IsReadFailure(e))
            {
                // git decides
            }
        }

        return _toolkit.HeadSha();
    }

    /// <summary>A file at the toolkit's main read in process (decoded as git's output is), else <c>git show</c>.</summary>
    public string? ToolkitFile(string path)
    {
        if (_noGit)
        {
            try
            {
                var file = Path.Combine(_toolkitDir, path.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(file) ? Text(File.ReadAllBytes(file)) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (_objects.Value is { } objects)
        {
            try
            {
                if (objects.Resolve(_toolkitRef) is not { } head)
                {
                    return null;
                }

                using var s = objects.Begin();
                return s.FindPath(head, path) is { Kind: TreeEntryKind.Blob } entry ? Text(s.ReadBlob(entry.Id)) : null;
            }
            catch (Exception e) when (InProcessHistory.IsReadFailure(e))
            {
                // git decides
            }
        }

        return _toolkit.ReadFile(path);
    }

    // INS-17: in a kit with no .git the past versions are the manifest's blob ids, matched against the copy's own blob id.
    private bool IsPastVersion(string template, string copy) =>
        _noGit
            ? TemplateHistory.Load(_toolkitDir)?.IsPastVersion(template, copy) == true
            : PastVersions(template).Contains(TemplateState.Hash(copy));

    /// <summary>The hashes of every version of the template on the toolkit's main, from its history.</summary>
    private HashSet<string> PastVersions(string template) => PastVersionsInProcess(template) ?? PastVersionsByGit(template);

    /// <summary>
    /// 1065-B (MM-14): every blob the template's path had on the toolkit's main, from a walk of its commits in
    /// process, kept between checks so each walks only the new commits (and reads each templates folder once).
    /// Null when git must be asked: no reader (MM-2), an object it cannot read, or a merge in the history, where
    /// git's history simplification could leave out a version the walk would find.
    /// </summary>
    public HashSet<string>? PastVersionsInProcess(string template)
    {
        if (_objects.Value is not { } objects)
        {
            return null;
        }

        lock (_walkLock)
        {
            try
            {
                if (_merges || objects.Resolve(_toolkitRef) is not { } head)
                {
                    return null;
                }

                using var s = objects.Begin();
                Walk(s, head);
                if (_merges)
                {
                    return null;
                }

                var hashes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var blob in _versions.GetValueOrDefault(template) ?? [])
                {
                    if (!_blobHashes.TryGetValue(blob, out var hash))
                    {
                        hash = TemplateState.Hash(Text(s.ReadBlob(ObjectId.Parse(blob))));
                        _blobHashes[blob] = hash;
                    }

                    hashes.Add(hash);
                }

                return hashes;
            }
            catch (Exception e) when (InProcessHistory.IsReadFailure(e))
            {
                return null;
            }
        }
    }

    // Every commit from head not walked yet: each templates folder not seen yet gives each template's blob there.
    private void Walk(ObjectReadSession s, ObjectId head)
    {
        var todo = new Stack<ObjectId>();
        todo.Push(head);
        var found = new List<(ObjectId Commit, ObjectId? Tree)>();
        var seen = new HashSet<ObjectId>();
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (_walked.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            var c = s.ReadCommit(id);
            if (c.Parents.Count > 1)
            {
                _merges = true;
                return;
            }

            var dir = s.FindPath(c.Tree, GenericFiles.TemplatesDir);
            found.Add((id, dir is { Kind: TreeEntryKind.Tree } ? dir.Id : null));
            foreach (var parent in c.Parents)
            {
                todo.Push(parent);
            }
        }

        foreach (var (commit, tree) in found)
        {
            if (tree is { } t && _templateTrees.Add(t))
            {
                foreach (var f in Files)
                {
                    if (s.FindPath(t, f.Template) is { Kind: TreeEntryKind.Blob } entry)
                    {
                        if (!_versions.TryGetValue(f.Template, out var set))
                        {
                            _versions[f.Template] = set = new HashSet<string>(StringComparer.Ordinal);
                        }

                        set.Add(entry.Id.ToString());
                    }
                }
            }

            _walked.Add(commit);
        }
    }

    // A blob's text as git's output reaches us through GitRunner: UTF-8, a byte order mark taken off.
    private static string Text(byte[] data)
    {
        using var reader = new StreamReader(new MemoryStream(data), new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>The hashes of every version of the template on the toolkit's main, from <c>git log --raw</c>.</summary>
    public HashSet<string> PastVersionsByGit(string template)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var r = _git.Run(_toolkitDir, "log", "--format=", "--raw", "--no-abbrev", "--no-renames", _toolkitRef, "--", $"{GenericFiles.TemplatesDir}/{template}");
        if (!r.Ok)
        {
            return hashes;
        }

        // ":<mode> <mode> <old blob> <new blob> <status>\t<path>"
        foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t')[0].Split(' ');
            if (!line.StartsWith(':') || fields.Length < 4)
            {
                continue;
            }

            foreach (var blob in new[] { fields[2], fields[3] }.Where(b => b.Trim('0').Length > 0))
            {
                if (!_blobHashes.TryGetValue(blob, out var hash))
                {
                    var text = _toolkit.ReadBlob(blob);
                    if (text is null)
                    {
                        continue;
                    }

                    hash = TemplateState.Hash(text);
                    _blobHashes[blob] = hash;
                }

                hashes.Add(hash);
            }
        }

        return hashes;
    }

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;
}
