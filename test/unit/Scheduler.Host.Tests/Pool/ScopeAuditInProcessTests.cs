using Scheduler.Core.Scopes;
using Scheduler.Host.Git;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// 1065-B, state-cache.md MM-14: the scope audit compares the two commits' trees in process - the same files as
/// <c>git diff --name-only</c> - and starts no git; a commit given by name is still left to git.
/// </summary>
public sealed class ScopeAuditInProcessTests : IDisposable
{
    private readonly GitFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public void MM_14_ScopeAudit_InProcessMatchesGitDiff_AndStartsNoGit()
    {
        _fx.Push("one", new Dictionary<string, string?> { ["src/a.txt"] = "1\n", ["docs/b.md"] = "1\n", ["x/gone.txt"] = "1\n" });
        var from = GitFixture.Git(_fx.Seed, "rev-parse", "HEAD");
        _fx.Push("two", new Dictionary<string, string?>
        {
            ["src/a.txt"] = "2\n", ["docs/b.md"] = "2\n", ["x/gone.txt"] = null, ["src/new ü.txt"] = "1\n", ["docs/known_gaps.md"] = "- g\n",
        });
        var to = GitFixture.Git(_fx.Seed, "rev-parse", "HEAD");
        var git = new GitRunner();
        var audit = new ScopeAudit(git);

        var mine = audit.ChangedInProcess(_fx.Seed, from, to);
        // git's bytes read as UTF-8, as the scheduler reads them (its console is UTF-8, ConsoleOutput); a test
        // host's console code page would garble the non-ASCII name.
        var byGit = System.Text.Encoding.UTF8.GetString(new GitRunner().RunBinary(_fx.Seed, [], "-c", "core.quotepath=off", "diff", "--name-only", from, to)!)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(byGit.Order(StringComparer.Ordinal), mine!.Order(StringComparer.Ordinal));

        var lines = new List<string>();
        var outside = audit.Check(_fx.Seed, "1-B-x", Scope.Of("src/") with { AddOnly = ["docs/known_gaps.md"] }, from, to, lines.Add);
        Assert.Equal(["docs/b.md"], outside); // x/gone.txt reappears unchanged as src/new ü.txt: git lists the new name only
        Assert.Single(lines);
        Assert.Equal(0, git.Started);

        Assert.Null(audit.ChangedInProcess(_fx.Seed, "HEAD~1", "HEAD"));

        // A deleted file and a new one that differ: maybe an inexact rename, which git's own detection settles.
        _fx.Push("three", new Dictionary<string, string?> { ["src/a.txt"] = null, ["src/renamed.txt"] = "2\nmore\n" });
        Assert.Null(audit.ChangedInProcess(_fx.Seed, to, GitFixture.Git(_fx.Seed, "rev-parse", "HEAD")));
    }
}
