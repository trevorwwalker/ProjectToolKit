// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

public class SlotWorktreesTests
{
    private static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo(Scheduler.Host.Git.GitExecutable.Path) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false }; _ = Scheduler.Host.Git.GitExecutable.Apply(psi);
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "-c", "safe.bareRepository=all" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        // No input, ever: a git that reads stdin would otherwise read the terminal the tests were started from.
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {o}{err.Result}");
        return o.Trim();
    }

    // WRK-5: slot = worktree <workspaceRoot>\slotN on branch slotN, reset to the remote's main before every card.
    [Fact]
    public void Workers_PrepareSlot_CreatesAndResetsWorktree()
    {
        using var temp = new TempDir();
        var bare = Path.Combine(temp.Path, "remote.git");
        var repo = Path.Combine(temp.Path, "repo");
        var roots = Path.Combine(temp.Path, "ws");
        Directory.CreateDirectory(bare);
        Directory.CreateDirectory(repo);
        Git(bare, "init", "-q", "--bare", "-b", "main");
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "remote", "add", "origin", bare);
        File.WriteAllText(Path.Combine(repo, "a.txt"), "one");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "-m", "one");
        Git(repo, "push", "-q", "origin", "main");
        Git(repo, "fetch", "-q", "origin");

        var slots = new SlotWorktrees(repo, roots, "origin", "main");
        var dir = slots.Prepare(1);

        Assert.Equal(Path.Combine(roots, "slot1"), dir);
        Assert.Equal("slot1", Git(dir, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(Git(bare, "rev-parse", "main"), Git(dir, "rev-parse", "HEAD"));

        // Second run: a dirty file, an untracked file, a stale commit on the slot branch, and a new main.
        File.WriteAllText(Path.Combine(dir, "a.txt"), "dirty");
        File.WriteAllText(Path.Combine(dir, "untracked.txt"), "u");
        File.WriteAllText(Path.Combine(dir, "stale.txt"), "s");
        Git(dir, "add", "stale.txt");
        Git(dir, "commit", "-q", "-m", "stale");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "two");
        Git(repo, "commit", "-q", "-am", "two");
        Git(repo, "push", "-q", "origin", "main");

        Assert.Equal(dir, slots.Prepare(1));

        Assert.Equal("slot1", Git(dir, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(Git(bare, "rev-parse", "main"), Git(dir, "rev-parse", "HEAD"));
        Assert.Equal("", Git(dir, "status", "--porcelain"));
        Assert.Equal("two", File.ReadAllText(Path.Combine(dir, "a.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "untracked.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "stale.txt")));
    }

    // 1119-B: a slot folder that is a plain folder inside an outer repository is set aside and re-created as a
    // worktree; the outer repository gets no commit and no reset.
    [Fact]
    public void Prepare_PlainSlotFolderInsideOuterRepository_IsRecreatedAsWorktree()
    {
        using var temp = new TempDir();
        var outer = temp.Path;
        Git(outer, "init", "-q", "-b", "master");
        Git(outer, "commit", "-q", "--allow-empty", "-m", "initial");
        var bare = Path.Combine(outer, "remote.git");
        var repo = Path.Combine(outer, "repo");
        var roots = Path.Combine(outer, "ws");
        Directory.CreateDirectory(bare);
        Directory.CreateDirectory(repo);
        Git(bare, "init", "-q", "--bare", "-b", "main");
        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "remote", "add", "origin", bare);
        File.WriteAllText(Path.Combine(repo, "a.txt"), "one");
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "-m", "one");
        Git(repo, "push", "-q", "origin", "main");
        Git(repo, "fetch", "-q", "origin");
        var slot = Path.Combine(roots, "slot2");
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, "left.txt"), "left");

        var dir = new SlotWorktrees(repo, roots, "origin", "main").Prepare(2);

        Assert.Equal(slot, dir);
        Assert.True(Scheduler.Host.Git.GitRunner.IsWorktreeOf(dir, repo));
        Assert.Equal("slot2", Git(dir, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal("one", File.ReadAllText(Path.Combine(dir, "a.txt")));
        var aside = Assert.Single(Directory.GetDirectories(roots, "slot2.not-a-worktree-*"));
        Assert.Equal("left", File.ReadAllText(Path.Combine(aside, "left.txt")));
        Assert.Equal("1", Git(outer, "rev-list", "--count", "HEAD"));
        Assert.NotNull(Scheduler.Host.Git.GitRunner.CeilingFor(dir));
    }
}
