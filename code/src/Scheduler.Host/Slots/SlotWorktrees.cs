using System.Diagnostics;

namespace Scheduler.Host.Slots;

/// <summary>A slot is a git worktree on its own branch, reset to the remote's main before every card (WRK-5).</summary>
public sealed class SlotWorktrees
{
    private readonly string _repo;
    private readonly string _workspaceRoot;
    private readonly string _remote;
    private readonly string _refMain;

    /// <summary>Slots of <paramref name="repo"/> live in <paramref name="workspaceRoot"/>; they follow <c>remote/branch</c>.</summary>
    public SlotWorktrees(string repo, string workspaceRoot, string remote, string branch)
    {
        _repo = repo;
        _workspaceRoot = workspaceRoot;
        _remote = remote;
        _refMain = $"{remote}/{branch}";
    }

    /// <summary>The slot's folder.</summary>
    public string SlotDirectory(int slot) => Path.Combine(_workspaceRoot, $"slot{slot}");

    /// <summary>
    /// WT-W1, WT-W2: the folder a starting worker takes - the lowest of <c>slot1</c> ..
    /// <c>slot&lt;maxWorkers&gt;</c> that no running worker holds, whatever its type. A worker
    /// adopted on a folder above <paramref name="maxWorkers"/> (WT-W4, WT-W5) keeps it and is
    /// not in the way. Null when every folder is held, which WT-1 makes impossible.
    /// </summary>
    public static int? FreeFolder(IEnumerable<int> held, int maxWorkers)
    {
        var taken = new HashSet<int>(held);
        for (var folder = 1; folder <= maxWorkers; folder++)
        {
            if (!taken.Contains(folder))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>Create the worktree if missing, then fetch, check out, reset and clean it. Returns its folder (WRK-5).</summary>
    /// <remarks>
    /// 1119-B: a slot folder that is not a worktree of the repository (a plain folder) is set aside and the worktree
    /// added again; git in the slot gets <c>GIT_CEILING_DIRECTORIES</c>, so it can never reach an outer repository.
    /// </remarks>
    public string Prepare(int slot)
    {
        var dir = SlotDirectory(slot);
        var branch = $"slot{slot}";
        Scheduler.Host.Git.GitRunner.Confine(dir);
        if (!Scheduler.Host.Git.GitRunner.IsWorktreeOf(dir, _repo))
        {
            Directory.CreateDirectory(_workspaceRoot);
            var aside = Scheduler.Host.Git.GitRunner.SetAside(dir);
            Git(_repo, "worktree", "prune");
            var (code, output) = Git(_repo, "worktree", "add", "-f", "-B", branch, dir, _refMain);
            if (code != 0 || !Scheduler.Host.Git.GitRunner.IsWorktreeOf(dir, _repo))
            {
                throw new InvalidOperationException($"worktree add for slot {slot} failed: {output}");
            }

            if (aside is not null)
            {
                Console.Error.WriteLine($"slot {slot}: {dir} was not a worktree; it was moved to {aside} and the worktree added again");
            }
        }
        Git(dir, "fetch", "-q", _remote);
        // 1067-B (MM-17): one forced checkout moves the branch, the index and the files (was checkout, then reset --hard).
        Git(dir, "checkout", "-q", "-f", "-B", branch, _refMain);
        Git(dir, "clean", "-q", "-fd");
        return dir;
    }

    private static (int Code, string Output) Git(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(Scheduler.Host.Git.GitExecutable.Path)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = !Scheduler.Host.Git.OwnConsole.Attached,   // inherit our console: no conhost.exe per git
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        _ = GitNonInteractive.Apply(psi); // 1073-B: never an editor
        _ = Scheduler.Host.Git.GitRunner.ApplyCeiling(psi); // 1119-B: never above the slot
        _ = Scheduler.Host.Git.GitExecutable.Apply(psi);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, (output + err.Result).Replace('\n', ' ').Trim());
    }
}
