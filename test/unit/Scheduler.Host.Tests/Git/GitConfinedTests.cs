using System.Diagnostics;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1119-B: git the scheduler starts in a folder it owns never walks up to an outer repository
/// (<c>GIT_CEILING_DIRECTORIES</c>), and a folder is known to be a worktree of the project in process.
/// </summary>
public class GitConfinedTests
{
    // An outer repository with one commit, as C:\Projects is.
    private static string Outer(TempDir temp)
    {
        var outer = Path.Combine(temp.Path, "outer");
        Directory.CreateDirectory(outer);
        GitFixture.Git(outer, "init", "-q", "-b", "master");
        GitFixture.Git(outer, "commit", "-q", "--allow-empty", "-m", "initial");
        return outer;
    }

    [Fact]
    public void ConfinedFolder_PlainFolderInsideOuterRepository_GitDoesNotReachIt()
    {
        using var temp = new TempDir();
        var outer = Outer(temp);
        var plain = Path.Combine(outer, "ws", "answers");
        var other = Path.Combine(outer, "ws", "other");
        Directory.CreateDirectory(plain);
        Directory.CreateDirectory(other);
        var git = new GitRunner();

        // Unconfined, git walks up to the outer repository: the fault.
        var before = git.Run(other, "rev-parse", "--show-toplevel");
        Assert.True(before.Ok, before.Stderr);
        Assert.Equal(Path.GetFullPath(outer), Path.GetFullPath(before.Stdout.Trim()));

        GitRunner.Confine(plain);
        Assert.Equal(Path.GetFullPath(Path.Combine(outer, "ws")), GitRunner.CeilingFor(plain));
        Assert.Equal(GitRunner.CeilingFor(plain), GitRunner.CeilingFor(plain + Path.DirectorySeparatorChar));
        Assert.Null(GitRunner.CeilingFor(other));

        var after = git.Run(plain, "rev-parse", "--show-toplevel");
        Assert.False(after.Ok, after.Stdout);
        Assert.False(git.Run(plain, "commit", "-q", "--allow-empty", "-m", "stray").Ok);
        Assert.Equal("initial", GitFixture.Git(outer, "log", "-1", "--format=%s"));
    }

    [Fact]
    public void ApplyCeiling_SetsTheVariableOnlyForConfinedFolders()
    {
        using var temp = new TempDir();
        var mine = Path.Combine(temp.Path, "slot7");
        GitRunner.Confine(mine);
        var psi = GitRunner.ApplyCeiling(new ProcessStartInfo(GitExecutable.Path) { WorkingDirectory = mine });
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp.Path)), psi.Environment["GIT_CEILING_DIRECTORIES"]);
        var free = GitRunner.ApplyCeiling(new ProcessStartInfo(GitExecutable.Path) { WorkingDirectory = temp.Path });
        Assert.False(free.Environment.ContainsKey("GIT_CEILING_DIRECTORIES"));
    }

    [Fact]
    public void IsWorktreeOf_TellsWorktreesFromPlainFoldersAndOtherRepositories()
    {
        using var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { ["a.txt"] = "1\n" });
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var root = Path.GetDirectoryName(fx.Repo)!;
        var tree = Path.Combine(root, "ws", "answers");
        GitFixture.Git(fx.Repo, "worktree", "add", "-q", "-f", "-B", "answers", tree, "origin/main");

        Assert.True(GitRunner.IsWorktreeOf(tree, fx.Repo));
        Assert.False(GitRunner.IsWorktreeOf(tree, fx.Seed));          // a worktree of another repository
        Assert.False(GitRunner.IsWorktreeOf(fx.Seed, fx.Repo));        // a repository of its own (.git folder)
        var plain = Path.Combine(root, "ws", "plain");
        Directory.CreateDirectory(plain);
        Assert.False(GitRunner.IsWorktreeOf(plain, fx.Repo));          // a plain folder
        File.WriteAllText(Path.Combine(plain, ".git"), "gitdir: " + Path.Combine(root, "gone"));
        Assert.False(GitRunner.IsWorktreeOf(plain, fx.Repo));          // a .git file pointing nowhere
    }

    [Fact]
    public void SetAside_MovesAFullFolderAndRemovesAnEmptyOne()
    {
        using var temp = new TempDir();
        var full = Path.Combine(temp.Path, "answers");
        Directory.CreateDirectory(Path.Combine(full, "jobs"));
        File.WriteAllText(Path.Combine(full, "jobs", "README.md"), "x");
        var to = GitRunner.SetAside(full);
        Assert.NotNull(to);
        Assert.False(Directory.Exists(full));
        Assert.True(File.Exists(Path.Combine(to, "jobs", "README.md")));

        Directory.CreateDirectory(full);
        Assert.Null(GitRunner.SetAside(full));
        Assert.False(Directory.Exists(full));
        Assert.Null(GitRunner.SetAside(full));
    }
}
