// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Moves;
using Scheduler.Host.Git;

namespace Scheduler.Host.Moves;

/// <summary>
/// Every scheduler commit goes through the <c>answers</c> worktree (MOVE-1, Answers-Tree L1150):
/// it is reset to the remote's main, the change is made, only the change's paths are committed and
/// pushed to <c>HEAD:&lt;branch&gt;</c>; a rejected push starts again from the new main, up to
/// three tries. A card change from the page (Update-Card L1204) first replays the developer's
/// uncommitted edits to the card (MOVE-4) and works its new text out from main's copy on each try.
/// 1064-B (state-cache.md MM-11): a push that went through is handed to the repository's model of main - the new
/// commit, its parent and the paths it wrote or removed - which takes those paths' blobs as committed, in process,
/// and moves to the commit with no diff and no git process; a rejected or failed push leaves the model as it was.
/// </summary>
public sealed class AnswersCommitter : IJobsCommitter
{
    /// <summary>How many times a commit is tried before it is given up (MOVE-1).</summary>
    public const int Tries = 3;

    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly GitRunner _git;
    private readonly string _repo;
    private readonly string _remote;
    private readonly string _branch;
    private readonly ISchedulerLog? _log;
    private bool _notWorktreeLogged;

    /// <param name="git">Runs git.</param>
    /// <param name="repo">The developer's checkout of the repository (the scheduler's <c>$Repo</c>).</param>
    /// <param name="answersDir">Where the answers worktree is kept (<c>&lt;slot root&gt;\answers</c>).</param>
    /// <param name="remote">The remote, <c>origin</c>.</param>
    /// <param name="branch">The main branch, <c>main</c>.</param>
    /// <param name="log">Where a pushed card change is logged, or null.</param>
    public AnswersCommitter(GitRunner git, string repo, string answersDir, string remote, string branch, ISchedulerLog? log = null)
    {
        _git = git;
        _repo = repo;
        AnswersDir = answersDir;
        _remote = remote;
        _branch = branch;
        _log = log;
        GitRunner.Confine(answersDir); // 1119-B: git there never reaches an outer repository
        MainModel = () => IMainFeed.Shared(_repo, _remote, _branch) is MainModelsRepository { Model: { } model } ? model : null;
    }

    /// <summary>The developer's checkout of the repository: the key of its model of main.</summary>
    public string Repo => _repo;

    /// <summary>
    /// 1064-B: the repository's model of main a push is handed to (MM-11), or null when there is none yet
    /// (<see cref="MainModels.Shared"/>'s unless a test sets one).
    /// </summary>
    public Func<RepositoryModel?> MainModel { get; set; }

    /// <summary>The answers worktree's folder.</summary>
    public string AnswersDir { get; }

    /// <summary>
    /// 987-B: the one writer to main, or null to commit on the caller's thread. When set, every commit
    /// - a page action's, the tick's, the planner's - takes its turn there, so two never meet in the
    /// answers worktree; on the writer's own thread it runs at once.
    /// </summary>
    public Scheduler.Host.Git.GitWriter? Writer { get; set; }

    private string RefMain => $"{_remote}/{_branch}";

    /// <summary>
    /// The answers worktree, made if missing, fetched and reset to the remote's main with nothing
    /// left over (Answers-Tree L1150). Returns its folder.
    /// 1119-B: a folder there that is not a worktree of the repository (a plain folder, left when an earlier
    /// <c>worktree add</c> failed) is set aside and the worktree added again; when that fails it is logged once
    /// and no git runs there.
    /// </summary>
    public string AnswersTree()
    {
        if (!GitRunner.IsWorktreeOf(AnswersDir, _repo))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(AnswersDir))!);
            var aside = GitRunner.SetAside(AnswersDir);
            _git.Run(_repo, "fetch", "-q", _remote);
            _git.Run(_repo, "worktree", "prune");
            var add = _git.Run(_repo, "worktree", "add", "-f", "-B", "answers", AnswersDir, RefMain);
            if (!GitRunner.IsWorktreeOf(AnswersDir, _repo))
            {
                if (!_notWorktreeLogged)
                {
                    _notWorktreeLogged = true;
                    _log?.Write($"answers: {AnswersDir} is not a worktree of {_repo} and worktree add failed ({Out(add)}); no git runs there");
                }

                return AnswersDir;
            }

            _notWorktreeLogged = false;
            if (aside is not null)
            {
                _log?.Write($"answers: {AnswersDir} was not a worktree; it was moved to {aside} and the worktree added again");
            }
        }

        _git.Run(AnswersDir, "fetch", "-q", _remote);
        // 1067-B (MM-17): one forced checkout moves the branch, the index and the files (was checkout, then reset --hard).
        _git.Run(AnswersDir, "checkout", "-q", "-f", "-B", "answers", RefMain);
        _git.Run(AnswersDir, "clean", "-q", "-fd");
        return AnswersDir;
    }

    /// <summary>The clock for the line CM-24 adds to a held card's Result.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// <summary>
    /// CM-24 (card 1300-B; the developer: "cards that haven't pushed to main can't be done"): an edit that moves the
    /// card <paramref name="name"/> into done/ - an answer from the page whose choice ends <c>-&gt; done</c>, the Done
    /// button on a review card, a move request - while a <c>wip/</c> branch of the card holds work main lacks goes to
    /// queue/ instead, as <c>waiting: planner (finished, but its work is not on main: &lt;branches&gt;)</c>, with a line in
    /// its Result saying who asked for done. Its other changes (the answer written after the question) are kept. Git
    /// is asked only for a move to done/; when it cannot say, the edit is applied as asked, with a log line.
    /// </summary>
    private CardEdit Hold(string name, CardEdit edit)
    {
        if (edit.Folder != CardFolder.Done || !edit.Moves)
        {
            return edit;
        }

        var where = new UnpushedWork(_git, _remote, _branch).Wip(AnswersDir, name);
        if (where is null)
        {
            _log?.Write($"{name}: could not tell whether its wip branches are on {_branch}; moved to done/ as asked");
            return edit;
        }

        if (where.Length == 0)
        {
            return edit;
        }

        var who = edit.Message.StartsWith("jobs: answer ", StringComparison.Ordinal) ? "an answer from the page"
            : edit.Message.EndsWith("(reviewed from the page)", StringComparison.Ordinal) ? "the Done button"
            : "a move request";
        var reason = UnpushedWork.Words(_branch, where);
        var status = UnpushedWork.Waiting(reason);
        var at = edit.NewPath.LastIndexOf("/done/", StringComparison.Ordinal);
        var newPath = edit.NewPath[..at] + "/queue/" + edit.NewPath[(at + "/done/".Length)..];
        var line = $"{who} asked for done/, but its work is not on {_branch}: {where}; it waits on the planner (CM-24).";
        return edit with
        {
            NewPath = newPath,
            Folder = CardFolder.Queue,
            Status = status,
            Text = UnpushedWork.Journal(CardText.SetStatus(edit.Text, status), line, Now()),
            Message = $"jobs: wait {name} ({who} asked for done; its work is not on {_branch})",
            Log = $"{name}: {who} asked for done/, but its work is not on {_branch}: {where}; it goes to queue/ to wait on the planner (CM-24)",
            ClearsRetries = false,
        };
    }

    /// <summary>MOVE-1: <paramref name="change"/> as one commit on main, as it is, tried up to three times.</summary>
    public CommitResult Apply(JobsChange change)
    {
        var job = new Job(change.Message, change);
        Run("commit: " + change.Message, StepOf(job));
        return job.Result!;
    }

    /// <summary>
    /// 1066-B (MM-13): <paramref name="changes"/> ready together, one commit each in their order and one push for all.
    /// A rejected push redoes them one by one from the new main, so one bad change never drops the others; each has its
    /// own result, in the order given. On the writer it is one turn.
    /// </summary>
    public IReadOnlyList<CommitResult> ApplyAll(IReadOnlyList<JobsChange> changes)
    {
        if (changes.Count == 0)
        {
            return [];
        }

        var jobs = changes.Select(c => new Job(c.Message, c)).ToList();
        var steps = jobs.Select(StepOf).ToList();
        if (Writer is { } writer)
        {
            writer.Run("commits: " + changes[0].Message, () => GitWriter.RunGroup(steps));
        }
        else
        {
            GitWriter.RunGroup(steps);
        }

        return [.. jobs.Select(j => j.Result!)];
    }

    /// <summary>
    /// A card change from the page (Update-Card L1204, MOVE-1, MOVE-4). On each try the answers
    /// worktree is reset to main, the developer's uncommitted edits to the card (taken once, from
    /// the repository and the answers worktree) are replayed with <c>git apply --3way</c>, or
    /// appended when they only add at the end; <paramref name="edit"/> then gets main's text of the
    /// card so replayed, and its change is committed and pushed. Once pushed the repository's copy
    /// of the card is reverted (main has the edit). An edit that cannot be merged is an error and
    /// nothing is committed.
    /// </summary>
    /// <param name="path">The card's path on main, as <c>jobs/active/010-A-x.md</c>.</param>
    /// <param name="edit">Works the change out from main's text (Core's <see cref="CardMoves"/>).</param>
    /// <param name="replayLocalEdits">
    /// False for a change that does not replay the developer's edits: Answer-Card (L1076) resets
    /// the answers worktree and commits without them.
    /// </param>
    /// <param name="made">
    /// Card 1300-B: told of the edit as it is committed, after CM-24 may have turned a move to done/ into a wait on the
    /// planner; null when the caller does not need it.
    /// </param>
    public CommitResult UpdateCard(string path, Func<string, CardEdit> edit, bool replayLocalEdits = true, Action<CardEdit>? made = null)
    {
        var job = CardJob(new CardUpdate(Path.GetFileNameWithoutExtension(path), () => path, edit, replayLocalEdits, made));
        Run("update " + Path.GetFileName(path), StepOf(job));
        return job.Result!;
    }

    /// <summary>
    /// 1190-B (MM-13): card changes ready together, as <see cref="UpdateCard"/> does each, one commit each in their
    /// order and one push for all. Each update's path is found on the answers worktree as its turn comes, so one may
    /// move a card an earlier one moved. A rejected push redoes them one by one from the new main; each has its own
    /// result, in the order given, so a failed one never loses the others. On the writer it is one turn.
    /// </summary>
    public IReadOnlyList<CommitResult> UpdateCards(IReadOnlyList<CardUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return [];
        }

        var jobs = updates.Select(CardJob).ToList();
        var steps = jobs.Select(StepOf).ToList();
        if (Writer is { } writer)
        {
            writer.Run("updates: " + updates[0].Name, () => GitWriter.RunGroup(steps));
        }
        else
        {
            GitWriter.RunGroup(steps);
        }

        return [.. jobs.Select(j => j.Result!)];
    }

    private Job CardJob(CardUpdate update)
    {
        var name = update.Name;
        var edit = update.Edit;
        var replayLocalEdits = update.ReplayLocalEdits;
        string? path = null;
        List<LocalEdit>? patches = null;
        CardEdit? last = null;
        var job = new Job(name, null) { PlainName = true };
        job.Prepare = () =>
        {
            path = update.Path();
            if (path is null)
            {
                return Failed($"{name} is not on {_branch}");
            }

            patches ??= replayLocalEdits ? LocalEdits(path) : [];
            foreach (var p in patches)
            {
                var error = Replay(AnswersDir, path, name, p);
                if (error.Length > 0)
                {
                    return Failed(error);
                }
            }

            var file = Full(AnswersDir, path);
            if (!File.Exists(file))
            {
                return Failed($"{path} is no longer on {_branch}");
            }

            last = edit(File.ReadAllText(file, Utf8));
            if (!last.Ok)
            {
                return Failed(last.Error);
            }

            last = Hold(name, last);
            update.Made?.Invoke(last);

            job.Change = last.Change;
            return null;
        };
        job.AfterPushed = () =>
        {
            foreach (var _ in patches!.Where(p => p.Dir == _repo))
            {
                _git.Run(_repo, "checkout", "--", path!);
            }

            var line = last!.Log.Length > 0 ? last.Log : last.Message;
            _log?.Write(line + (patches!.Count > 0 ? " (with your edit to the card)" : ""));
        };
        return job;
    }

    private void Run(string key, GitWriter.PushStep step)
    {
        if (Writer is { } writer)
        {
            writer.RunPushed(key, step);
        }
        else
        {
            GitWriter.RunGroup([step]);
        }
    }

    /// <summary>One change on its way: what it is, how it turned out, and what its commit was.</summary>
    private sealed class Job(string name, JobsChange? change)
    {
        public string Name { get; } = name;

        public bool PlainName { get; init; }

        public JobsChange? Change { get; set; } = change;

        /// <summary>Works the change out on the answers worktree (a page edit); a result when it cannot, else null.</summary>
        public Func<CommitResult?>? Prepare { get; set; }

        public Action? AfterPushed { get; set; }

        public CommitResult? Result { get; set; }

        public string Sha { get; set; } = "";

        public string? Parent { get; set; }

        public List<string> Paths { get; } = [];
    }

    /// <summary>The group's step for <paramref name="job"/> (MM-13): commit now, push with the others, then its own result.</summary>
    private GitWriter.PushStep StepOf(Job job) => new(
        this,
        () => CommitOnly(job),
        () => _git.Run(AnswersDir, "push", "-q", _remote, $"HEAD:{_branch}").Ok,
        () => AnswersTree(),
        outcome =>
        {
            if (outcome == PushOutcome.Pushed)
            {
                job.Result = new CommitResult { Ok = true, Sha = job.Sha };
                Pushed(job.Sha, job.Parent, job.Paths);
                CleanWipBranches(AnswersDir, job.Change!, planner: !job.PlainName);
                job.AfterPushed?.Invoke();
            }
            else if (outcome == PushOutcome.PushFailed)
            {
                job.Result = Failed(job.PlainName
                    ? $"push of {job.Name} was rejected three times"
                    : $"push of \"{job.Name}\" was rejected three times");
            }
        });

    /// <summary>
    /// The developer's uncommitted edits to the card, in the repository's working tree and in the
    /// answers worktree, as patches (Local-Edits L1168). A conflict a failed try left behind
    /// (unmerged in the index) is not an edit and is skipped.
    /// </summary>
    private List<LocalEdit> LocalEdits(string path)
    {
        var patches = new List<LocalEdit>();
        foreach (var dir in new[] { _repo, AnswersDir })
        {
            if (!Path.Exists(Path.Combine(dir, ".git")))
            {
                continue;
            }

            if (_git.Run(dir, "ls-files", "-u", "--", path).Stdout.Trim().Length > 0)
            {
                continue;
            }

            var d = _git.Run(dir, "-c", "core.safecrlf=false", "diff", "--no-color", "--", path);
            if (!d.Ok || d.Stdout.Trim().Length == 0 || d.Stdout.Contains("diff --cc ", StringComparison.Ordinal))
            {
                continue;
            }

            var patch = d.Stdout.EndsWith('\n') ? d.Stdout : d.Stdout + "\n";
            patches.Add(new LocalEdit(dir, patch, AppendedText(dir, path)));
        }

        return patches;
    }

    /// <summary>
    /// The text an edit only adds at the end of the card, or "" when it changes anything else
    /// (Appended-Text L1190): the working copy, line endings made LF and trailing space trimmed,
    /// starts with the index's copy and is longer.
    /// </summary>
    private string AppendedText(string dir, string path)
    {
        var show = _git.Run(dir, "show", ":" + path);
        var file = Full(dir, path);
        if (!show.Ok || !File.Exists(file))
        {
            return "";
        }

        var b = show.Stdout.Replace("\r\n", "\n").TrimEnd();
        var c = File.ReadAllText(file, Utf8).Replace("\r\n", "\n").TrimEnd();
        if (c.Length <= b.Length || !c.StartsWith(b, StringComparison.Ordinal))
        {
            return "";
        }

        return c[b.Length..].Trim('\n');
    }

    /// <summary>One edit replayed in the answers worktree (Update-Card L1209-1227); "" or the error.</summary>
    private string Replay(string dir, string path, string name, LocalEdit p)
    {
        var pf = Path.Combine(Path.GetTempPath(), $"ptk-card-edit-{Guid.NewGuid():N}.patch");
        GitResult r;
        try
        {
            File.WriteAllText(pf, p.Patch, Utf8);
            r = _git.Run(dir, "apply", "--3way", "--whitespace=nowarn", pf);
        }
        finally
        {
            File.Delete(pf);
        }

        if (r.Ok)
        {
            return "";
        }

        _git.Run(dir, "reset", "-q", "--", path);
        _git.Run(dir, "checkout", "-q", "HEAD", "--", path);
        if (p.Added.Length == 0)
        {
            var why = string.Join(' ', (r.Stdout + r.Stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2));
            return $"your edit to {name} could not be merged with main ({why}); pull, add it again and retry";
        }

        // Only added at the end: main's copy with the addition appended to it.
        var file = Full(dir, path);
        var text = File.ReadAllText(file, Utf8);
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        File.WriteAllText(file, text.TrimEnd() + eol + eol + p.Added.Replace("\n", eol) + eol, Utf8);
        return "";
    }

    /// <summary>
    /// Moves, writes and removals in the answers worktree and a commit of only their paths, no push (MM-13: the group
    /// pushes). False, with the job's result, when it cannot be made; what it left in the worktree is dropped.
    /// </summary>
    private bool CommitOnly(Job job)
    {
        job.Result = null;
        var failed = job.Prepare?.Invoke();
        if (failed is null && job.Change is not null)
        {
            failed = Stage(AnswersDir, job);
        }

        if (failed is null)
        {
            return true;
        }

        job.Result = failed;
        _git.Run(AnswersDir, "reset", "-q", "--hard", "HEAD");
        _git.Run(AnswersDir, "clean", "-q", "-fd");
        return false;
    }

    private CommitResult? Stage(string dir, Job job)
    {
        var change = job.Change!;
        job.Paths.Clear();
        var paths = job.Paths;
        foreach (var m in change.Moves)
        {
            _git.Run(dir, "add", "--", m.From);
            Directory.CreateDirectory(Path.GetDirectoryName(Full(dir, m.To))!);
            var mv = _git.Run(dir, "mv", m.From, m.To);
            if (!mv.Ok)
            {
                return Failed($"git mv failed: {Out(mv)}");
            }

            paths.Add(m.From);
            paths.Add(m.To);
        }

        foreach (var (path, text) in change.Writes)
        {
            var file = Full(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, Utf8);
            _git.Run(dir, "add", "--", path);
            paths.Add(path);
        }

        foreach (var path in change.Removals)
        {
            var rm = _git.Run(dir, "rm", "-q", "--", path);
            if (!rm.Ok)
            {
                return Failed($"git rm failed: {Out(rm)}");
            }

            paths.Add(path);
        }

        var args = new List<string> { "commit", "-q", "-m", change.Message.Replace('"', '\''), "--" };
        args.AddRange(paths.Distinct(StringComparer.Ordinal));
        var commit = _git.Run(dir, [.. args]);
        if (!commit.Ok)
        {
            return Failed($"commit failed: {Out(commit)}");
        }

        // The commit and its parent in the one rev-parse there always was: the model is moved by them (MM-11).
        var revs = _git.Run(dir, "rev-parse", "HEAD", "HEAD^").Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        job.Sha = revs.Length > 0 ? revs[0] : "";
        job.Parent = revs.Length > 1 ? revs[1] : null;
        return null;
    }

    /// <summary>
    /// MM-11: the pushed commit to the model. Not when the model has already seen it (the tick's ref read got there
    /// first, or a later push moved it on): applying it then would take the model back. Never fails the commit.
    /// </summary>
    private void Pushed(string sha, string? parent, IReadOnlyList<string> paths)
    {
        if (sha.Length == 0 || parent is null || MainModel() is not { } model || model.Commits(sha) is not null)
        {
            return;
        }

        model.Pushed(new PushedCommit(sha, parent, [.. paths.Distinct(StringComparer.Ordinal)]));
    }

    /// <summary>
    /// 1049-B: cards this change moved into <c>done/</c> lose their <c>wip/</c> branch on the remote when its work is
    /// on main; one that still holds work main lacks is kept and named in the card's Result. Never fails the commit.
    /// </summary>
    /// <param name="planner">
    /// Card 1300-B: the change came through <see cref="Apply"/>, whose only move into done/ is a planner batch's (the
    /// planner's evaluation, applied as it is); a card it moved there with work not on main is logged.
    /// </param>
    private void CleanWipBranches(string dir, JobsChange change, bool planner)
    {
        try
        {
            var done = change.Moves
                .Where(m => m.To.Contains("/done/", StringComparison.Ordinal) && m.To.EndsWith(".md", StringComparison.Ordinal))
                .ToDictionary(m => Path.GetFileNameWithoutExtension(m.To), m => m.To, StringComparer.Ordinal);
            if (done.Count == 0)
            {
                return;
            }

            var kept = new WipBranches(_git, _remote, _branch, _log).Clean(dir, done.Keys);
            foreach (var (card, files) in kept)
            {
                if (planner)
                {
                    _log?.Write($"planner: {card} moved to done with work not on {_branch}: {WipBranches.BranchOf(card)}");
                }

                NoteKept(dir, done[card], WipBranches.BranchOf(card), files);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _log?.Write($"wip branch cleanup failed: {e.Message}");
        }
    }

    /// <summary>Adds "wip/&lt;card&gt; holds work not on main: files" to the card's Result and pushes it; a rejected push is only logged.</summary>
    private void NoteKept(string dir, string path, string branch, IReadOnlyList<string> files)
    {
        var file = Full(dir, path);
        var text = File.ReadAllText(file, Utf8);
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var line = $"- {branch} holds work not on {_branch}: {string.Join(", ", files)}";
        File.WriteAllText(file, text.TrimEnd() + nl + line + nl, Utf8);
        _git.Run(dir, "add", "--", path);
        var card = Path.GetFileNameWithoutExtension(path);
        if (_git.Run(dir, "commit", "-q", "-m", $"jobs: journal {card} ({branch} kept)", "--", path).Ok
            && !_git.Run(dir, "push", "-q", _remote, $"HEAD:{_branch}").Ok)
        {
            _log?.Write($"the note on {card} about {branch} could not be pushed");
        }
    }

    private static string Full(string dir, string path) => Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));

    private static string Out(GitResult r) => string.Join(' ', (r.Stdout + r.Stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static CommitResult Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 1190-B: one card change for <see cref="AnswersCommitter.UpdateCards"/>: its name (for the log and a rejected push),
/// the card's path on the answers worktree as its turn comes (null when it is not on main), and the edit of main's text.
/// </summary>
/// <param name="Name">The card's name, as <c>010-A-x</c>.</param>
/// <param name="Path">Finds the card's path, as <c>jobs/active/010-A-x.md</c>, or null.</param>
/// <param name="Edit">Works the change out from main's text.</param>
/// <param name="ReplayLocalEdits">True to replay the developer's uncommitted edits to the card first (MOVE-4).</param>
/// <param name="Made">Card 1300-B: told of the edit as it is committed (after CM-24), or null.</param>
public sealed record CardUpdate(string Name, Func<string?> Path, Func<string, CardEdit> Edit, bool ReplayLocalEdits = true,
    Action<CardEdit>? Made = null);
