using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1065-B, state-cache.md MM-14: history read in process gives what git gives - the name-status diff of two
/// trees, a commit's Card trailers, the first-parent range, the merge base and the wip branch's files not on
/// main - each checked against git itself on a real repository; where it cannot be exact it says so (null).
/// </summary>
public sealed class InProcessHistoryTests : IDisposable
{
    private readonly GitFixture _fx = new();

    private string Seed => _fx.Seed;

    public void Dispose() => _fx.Dispose();

    [Fact]
    public void MM_14_Diff_MatchesGitNameStatus_WithModeSymlinkAndSubmoduleChanges()
    {
        Write("a.txt", "1\n");
        Write("dir/b.txt", "1\n");
        Write("dir/sub/c.txt", "1\n");
        Write("gone.txt", "1\n");
        Write("x/y.txt", "1\n");
        var first = Commit("first");

        Write("a.txt", "2\n");
        File.Delete(Path.Combine(Seed, "gone.txt"));
        Write("dir/new.txt", "1\n");
        Write("dir-b.txt", "1\n");
        GitFixture.Git(Seed, "add", "-A");
        GitFixture.Git(Seed, "update-index", "--chmod=+x", "dir/b.txt");
        var blob = GitFixture.Git(Seed, "hash-object", "-w", "--stdin", "--path", "a.txt");
        GitFixture.Git(Seed, "update-index", "--add", "--cacheinfo", $"120000,{blob},link");
        GitFixture.Git(Seed, "update-index", "--add", "--cacheinfo", $"160000,{first},module");
        GitFixture.Git(Seed, "rm", "-q", "-r", "--cached", "x");
        Directory.Delete(Path.Combine(Seed, "x"), true);
        Write("x", "now a file\n");
        GitFixture.Git(Seed, "add", "x");
        var second = CommitIndex("second");

        // Then the symlink becomes a plain file (a type change) and the submodule moves.
        GitFixture.Git(Seed, "update-index", "--cacheinfo", $"100644,{blob},link");
        GitFixture.Git(Seed, "update-index", "--cacheinfo", $"160000,{second},module");
        var third = CommitIndex("third");

        foreach (var (from, to) in new[] { (first, second), (second, third), (first, third) })
        {
            var git = GitFixture.Git(Seed, "-c", "core.quotePath=false", "diff-tree", "-r", "--no-renames", "--name-status", from, to)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('\t'))
                .Select(f => $"{f[0][0]} {f[1]}")
                .ToList();
            var objects = Objects();
            using var s = objects.Begin();
            var mine = InProcessHistory.Diff(s, s.TreeOf(ObjectId.Parse(from)), s.TreeOf(ObjectId.Parse(to)))
                .Select(c => $"{c.Status} {c.Path}")
                .ToList();
            Assert.Equal(git, mine);
        }
    }

    [Theory]
    [InlineData("subject only")]
    [InlineData("subject\n\nCard: 1-B-a")]
    [InlineData("subject\n\nbody text\n\nCard: 1-B-b\nCo-Authored-By: X <x@y.z>")]
    [InlineData("subject\n\nCard: 1-B-c\n\nnot trailers here")]
    [InlineData("subject\n\ncard: 1-B-d")]
    [InlineData("subject\n\nCard: 1-B-e\nCard: 1-B-f")]
    [InlineData("subject\n\nSome prose line\nCard: 1-B-g")]
    [InlineData("subject\n\nCard : 1-B-h")]
    [InlineData("Card: 1-B-i")]
    [InlineData("subject\n\nSigned-off-by: a <a@b.c>\nfree text\nCard: 1-B-j")]
    [InlineData("subject\n\nCard: 1-B-k\n  continued")]
    [InlineData("subject\n\nCo-Authored-By: X <x@y.z>\nCard:\n")]
    public void MM_14_CardTrailers_MatchGit(string message)
    {
        Write("f.txt", message);
        var sha = Commit(message);
        var git = GitFixture.Git(Seed, "log", "-1", "--format=%(trailers:key=Card,valueonly,separator=%x2c)", sha);
        var objects = Objects();
        var mine = string.Join(',', InProcessHistory.Trailers(objects.ReadCommit(ObjectId.Parse(sha)).Message, "Card"));
        Assert.Equal(git, mine);
    }

    [Fact]
    public void MM_14_FirstParentRange_MatchesRevList_AndAMergeIsLeftToGit()
    {
        var from = Commit("base", allowEmpty: true);
        Commit("one", allowEmpty: true);
        Commit("two", allowEmpty: true);
        var head = Commit("three", allowEmpty: true);
        var objects = Objects();
        using (var s = objects.Begin())
        {
            var range = InProcessHistory.FirstParentRange(s, ObjectId.Parse(from), ObjectId.Parse(head))!;
            Assert.Equal(GitFixture.Git(Seed, "rev-list", "--reverse", $"{from}..{head}").Split('\n'), range.Select(c => c.Id.ToString()));
            Assert.Empty(InProcessHistory.FirstParentRange(s, ObjectId.Parse(head), ObjectId.Parse(head))!);
        }

        GitFixture.Git(Seed, "checkout", "-q", "-b", "side", from);
        Commit("side", allowEmpty: true);
        GitFixture.Git(Seed, "checkout", "-q", "main");
        GitFixture.Git(Seed, "merge", "-q", "--no-ff", "-m", "merge", "side");
        var merged = Sha("HEAD");
        using (var s = objects.Begin())
        {
            Assert.Null(InProcessHistory.FirstParentRange(s, ObjectId.Parse(from), ObjectId.Parse(merged)));
        }
    }

    [Fact]
    public void MM_14_MergeBase_MatchesGit_AndACrissCrossIsLeftToGit()
    {
        var root = Commit("root", allowEmpty: true);
        var c1 = Commit("c1", allowEmpty: true);
        GitFixture.Git(Seed, "checkout", "-q", "-b", "b", root);
        var b1 = Commit("b1", allowEmpty: true);
        GitFixture.Git(Seed, "checkout", "-q", "main");
        var c2 = Commit("c2", allowEmpty: true);
        GitFixture.Git(Seed, "merge", "-q", "--no-ff", "-m", "merge b", "b");
        var m = Sha("HEAD");

        // Criss-cross: p and q each merge the other's first commit, so two bases are equally good.
        GitFixture.Git(Seed, "checkout", "-q", "-b", "p", c2);
        var p1 = Commit("p1", allowEmpty: true);
        GitFixture.Git(Seed, "checkout", "-q", "-b", "q", c2);
        var q1 = Commit("q1", allowEmpty: true);
        GitFixture.Git(Seed, "checkout", "-q", "p");
        GitFixture.Git(Seed, "merge", "-q", "--no-ff", "-m", "p takes q1", q1);
        var pm = Sha("HEAD");
        GitFixture.Git(Seed, "checkout", "-q", "q");
        GitFixture.Git(Seed, "merge", "-q", "--no-ff", "-m", "q takes p1", p1);
        var qm = Sha("HEAD");

        var objects = Objects();
        using var s = objects.Begin();
        foreach (var (a, b) in new[] { (b1, c2), (c1, b1), (m, b1), (b1, m), (c2, c2), (m, root), (pm, b1), (p1, q1) })
        {
            var (known, bas) = InProcessHistory.MergeBase(s, ObjectId.Parse(a), ObjectId.Parse(b));
            Assert.True(known, $"{a}..{b}");
            Assert.Equal(GitFixture.Git(Seed, "merge-base", a, b), bas?.ToString());
        }

        Assert.Equal(2, GitFixture.Git(Seed, "merge-base", "--all", pm, qm).Split('\n').Length);
        Assert.False(InProcessHistory.MergeBase(s, ObjectId.Parse(pm), ObjectId.Parse(qm)).Known);
    }

    [Fact]
    public void MM_14_WipBranch_NotOnMain_InProcessMatchesGit_AndStartsNoGit()
    {
        Write("a.txt", "1\n");
        Write("b.txt", "1\n");
        Write("c.txt", "1\n");
        Commit("base");
        GitFixture.Git(Seed, "push", "-q", "origin", "HEAD:main");
        GitFixture.Git(Seed, "checkout", "-q", "-b", "wip");
        Write("a.txt", "wip\n");          // only on the branch: kept
        Write("b.txt", "both\n");         // the same on main: not kept
        Write("c.txt", "wip\n");          // main changed it later: not kept
        Write("new.txt", "wip\n");        // new on the branch: kept
        Commit("wip work");
        GitFixture.Git(Seed, "push", "-q", "origin", "HEAD:refs/heads/wip/1-B");
        GitFixture.Git(Seed, "checkout", "-q", "main");
        Write("b.txt", "both\n");
        Write("c.txt", "main\n");
        Commit("main moves");
        GitFixture.Git(Seed, "push", "-q", "origin", "HEAD:main");
        GitFixture.Git(Seed, "fetch", "-q", "origin", "+refs/heads/wip/1-B:refs/remotes/origin/wip/1-B");

        var git = new GitRunner();
        var wip = new WipBranches(git, "origin", "main");
        var mine = wip.NotOnMainInProcess(Seed, "wip/1-B");
        Assert.Equal(0, git.Started);
        Assert.Equal(["a.txt", "new.txt"], mine);
        Assert.Equal(wip.NotOnMain(Seed, "origin/wip/1-B", "origin/main"), mine);
    }

    private MainObjects Objects() => MainObjects.TryOpen(Seed, out var why) ?? throw new InvalidOperationException(why);

    private void Write(string path, string text)
    {
        var full = Path.Combine(Seed, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private string Commit(string message, bool allowEmpty = false)
    {
        GitFixture.Git(Seed, "add", "-A");
        GitFixture.Git(Seed, allowEmpty ? new[] { "commit", "-q", "--allow-empty", "-m", message } : new[] { "commit", "-q", "-m", message });
        return Sha("HEAD");
    }

    // A commit of the index as it is (no add: the symlink and submodule entries exist only there).
    private string CommitIndex(string message)
    {
        GitFixture.Git(Seed, "commit", "-q", "-m", message);
        return Sha("HEAD");
    }

    private string Sha(string rev) => GitFixture.Git(Seed, "rev-parse", rev);
}
