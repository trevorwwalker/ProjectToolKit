using System.Diagnostics;

namespace Scheduler.Host.Git;

/// <summary>
/// Runs git the way the scheduler needs it (CFG-16): no prompts (<c>GIT_TERMINAL_PROMPT=0</c>,
/// <c>GCM_INTERACTIVE=Never</c>), stdin closed at once, stdout and stderr read separately, and a
/// time limit so a hung git never blocks the caller.
/// </summary>
public sealed class GitRunner
{
    private readonly string _exe;
    private readonly TimeSpan _timeout;
    private int _started;
    private static long s_total;

    /// <param name="exe">The git program; null = the one <see cref="GitExecutable"/> resolved (1043-B). Tests give a fake.</param>
    /// <param name="timeout">How long one call may run before it is killed; default 2 minutes.</param>
    public GitRunner(string? exe = null, TimeSpan? timeout = null)
    {
        _exe = exe ?? GitExecutable.Path;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    /// <summary>How many git processes this runner has started; tests count them (1013-B).</summary>
    public int Started => _started;

    /// <summary>MM-18: how many git processes every runner of the process has started (the tick's <c>git &lt;n&gt;</c>).</summary>
    public static long Total => Interlocked.Read(ref s_total);

    /// <summary>Run <c>git args</c> in <paramref name="workingDirectory"/>; exit code -1 when it cannot start or times out.</summary>
    public GitResult Run(string workingDirectory, params string[] args)
    {
        var psi = StartInfo(workingDirectory, args);
        try
        {
            using var p = Process.Start(psi)!;
            _ = Interlocked.Increment(ref _started);
            _ = Interlocked.Increment(ref s_total);
            p.StandardInput.Close();
            var err = p.StandardError.ReadToEndAsync();
            var outTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(_timeout))
            {
                p.Kill(true);
                return new GitResult(-1, "", "git timed out");
            }

            p.WaitForExit();
            return new GitResult(p.ExitCode, outTask.Result, err.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new GitResult(-1, "", e.Message);
        }
    }

    /// <summary>
    /// 1013-B: run <c>git args</c> with <paramref name="input"/> on its stdin and its stdout kept as bytes (for
    /// <c>cat-file --batch</c>, whose sizes count bytes); null when it cannot start, times out or fails.
    /// </summary>
    public byte[]? RunBinary(string workingDirectory, byte[] input, params string[] args)
    {
        var psi = StartInfo(workingDirectory, args);
        try
        {
            using var p = Process.Start(psi)!;
            _ = Interlocked.Increment(ref _started);
            _ = Interlocked.Increment(ref s_total);
            using var stdout = new MemoryStream();
            var outTask = p.StandardOutput.BaseStream.CopyToAsync(stdout);
            var err = p.StandardError.ReadToEndAsync();
            var inTask = Task.Run(() =>
            {
                try
                {
                    p.StandardInput.BaseStream.Write(input);
                    p.StandardInput.Close();
                }
                catch (IOException)
                {
                    // git ended before reading it all: its exit code says so
                }
            });
            if (!p.WaitForExit(_timeout))
            {
                p.Kill(true);
                return null;
            }

            p.WaitForExit();
            if (!inTask.Wait(_timeout) || !outTask.Wait(_timeout))
            {
                return null;
            }

            _ = err.Result;
            return p.ExitCode == 0 ? stdout.ToArray() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or AggregateException)
        {
            return null;
        }
    }

    private ProcessStartInfo StartInfo(string workingDirectory, string[] args)
    {
        var psi = new ProcessStartInfo(_exe)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = !OwnConsole.Attached,   // inherit our console: no conhost.exe per git
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "Never";
        _ = Scheduler.Host.Slots.GitNonInteractive.Apply(psi); // 1073-B: never an editor
        _ = ApplyCeiling(psi); // 1119-B: never above a folder the scheduler owns
        if (_exe == GitExecutable.Path)
        {
            _ = GitExecutable.Apply(psi); // 1043-B: the wrapper's PATH for hooks
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        return psi;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_confined =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 1119-B: <paramref name="folder"/> is a worktree the scheduler owns (a slot, the answers worktree). Every git
    /// started there from now on, by any runner, gets <c>GIT_CEILING_DIRECTORIES</c> set to the folder's parent, so
    /// when the folder is not a worktree git fails instead of walking up to an outer repository (<c>C:\Projects</c>).
    /// </summary>
    public static void Confine(string folder) => s_confined[Key(folder)] = 0;

    /// <summary>The ceiling for git started in <paramref name="workingDirectory"/>, or null when it is not a confined folder.</summary>
    public static string? CeilingFor(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
        {
            return null;
        }

        var key = Key(workingDirectory);
        return s_confined.ContainsKey(key) ? Path.GetDirectoryName(key) : null;
    }

    /// <summary>Set <c>GIT_CEILING_DIRECTORIES</c> on <paramref name="psi"/> when its folder is confined; returns it.</summary>
    public static ProcessStartInfo ApplyCeiling(ProcessStartInfo psi)
    {
        if (CeilingFor(psi.WorkingDirectory) is { } ceiling)
        {
            psi.Environment["GIT_CEILING_DIRECTORIES"] = ceiling;
        }

        return psi;
    }

    /// <summary>
    /// 1119-B, in process: <paramref name="folder"/> is the top of a worktree of <paramref name="repo"/> - its
    /// <c>.git</c> is a file whose <c>gitdir:</c> exists and lies in the repository's git folder. A plain folder,
    /// a broken <c>.git</c> file or a repository of its own (a <c>.git</c> folder) is not.
    /// </summary>
    public static bool IsWorktreeOf(string folder, string repo)
    {
        var gitdir = GitDirOf(Path.Combine(folder, ".git"));
        if (gitdir is null || !Directory.Exists(gitdir))
        {
            return false;
        }

        var common = CommonGitDir(repo);
        return common is null || IsUnder(gitdir, common);
    }

    /// <summary>
    /// 1119-B: move a folder that should have been a worktree but is not out of the way, beside it as
    /// <c>&lt;name&gt;.not-a-worktree-&lt;time&gt;</c>, so a worktree can be added there without deleting anything.
    /// An empty folder is removed. Returns where it went, or null when there was nothing to move or it could not be moved.
    /// </summary>
    public static string? SetAside(string folder)
    {
        var full = Key(folder);
        if (!Directory.Exists(full))
        {
            return null;
        }

        var to = $"{full}.not-a-worktree-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            if (!Directory.EnumerateFileSystemEntries(full).Any())
            {
                Directory.Delete(full);
                return null;
            }

            Directory.Move(full, to);
            return to;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The gitdir a .git file names, or null when it is missing, a folder, or not a gitdir file.
    private static string? GitDirOf(string dotGit)
    {
        if (!File.Exists(dotGit))
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(dotGit).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        const string prefix = "gitdir:";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var dir = text[prefix.Length..].Trim();
        return dir.Length == 0 ? null : Path.GetFullPath(dir, Path.GetDirectoryName(Path.GetFullPath(dotGit))!);
    }

    // The repository's common git folder (its .git, or the one its own worktree's gitdir shares), or null when unknown.
    private static string? CommonGitDir(string repo)
    {
        var dotGit = Path.Combine(repo, ".git");
        if (Directory.Exists(dotGit))
        {
            return Path.GetFullPath(dotGit);
        }

        var gitdir = GitDirOf(dotGit);
        if (gitdir is null)
        {
            return null;
        }

        var commondir = Path.Combine(gitdir, "commondir");
        try
        {
            return File.Exists(commondir) ? Path.GetFullPath(File.ReadAllText(commondir).Trim(), gitdir) : gitdir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUnder(string path, string parent)
    {
        var p = Key(path) + Path.DirectorySeparatorChar;
        var root = Key(parent) + Path.DirectorySeparatorChar;
        return p.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
