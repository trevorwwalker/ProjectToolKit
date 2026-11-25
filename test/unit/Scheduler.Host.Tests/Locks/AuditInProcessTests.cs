// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Core.Moves;
using Scheduler.Core.Scopes;
using Scheduler.Host.Git;
using Scheduler.Host.Locks;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1065-B, state-cache.md MM-14: the locks audit reads main's new commits in process - the same commits, paths,
/// new-file marks, Card trailers and own-commit marks as <c>git log --name-status</c> - and starts no git; a range
/// it cannot read exactly (a merge) is still read by git, with the same result.
/// </summary>
public sealed class AuditInProcessTests : IDisposable
{
    private const string Card = "1-B-audit";
    private const string Repo = "C:/Projects/Audited";
    private readonly GitFixture _fx = new();
    private readonly TempDir _log = new();
    private readonly FakeClock _clock = new(DateTimeOffset.Now);
    private readonly LockTableFile _table;

    public AuditInProcessTests()
    {
        _fx.Push("seed", new Dictionary<string, string?> { ["src/held/a.txt"] = "1\n", ["src/other/b.txt"] = "1\n" });
        _table = new LockTableFile(_log.Path, Repo, null);
        _table.Save(LockTable.Empty(Repo).Start(Card, new CardAge(1, 1), Scope.Of("src/held/"), _clock.Now.AddMinutes(-5), "p", 1)
            with { AuditedCommit = Head() });
    }

    public void Dispose()
    {
        _fx.Dispose();
        _log.Dispose();
    }

    [Fact]
    public void MM_14_AuditCommits_MatchGitLogNameStatus()
    {
        var from = Head();
        _fx.Push($"change a\n\nCard: {Card}\nCo-Authored-By: X <x@y.z>", new Dictionary<string, string?> { ["src/held/a.txt"] = "2\n", ["src/held/new.txt"] = "n\n" });
        _fx.Push($"jobs: done {Card}", new Dictionary<string, string?> { [$"jobs/done/{Card}.md"] = "# done\n" });
        _fx.Push("by hand", new Dictionary<string, string?> { ["src/other/b.txt"] = null, ["src/other/ü c.txt"] = "1\n" });
        _fx.Push("two cards\n\nCard: 2-C-x\nCard: 3-D-y", new Dictionary<string, string?> { ["docs/known_gaps.md"] = "- gap\n" });
        var head = Head();

        var mine = Audit(new GitRunner()).ReadInProcess(from, head);
        Assert.NotNull(mine);
        Assert.Equal(FromGit(from, head), mine.Select(Line));
    }

    [Fact]
    public void MM_14_Audit_InProcess_StartsNoGit_AndFindsTheStray()
    {
        _fx.Push($"change b\n\nCard: {Card}", new Dictionary<string, string?> { ["src/other/b.txt"] = "2\n" });
        var git = new GitRunner();
        var stray = Assert.Single(Audit(git).Run(Head()));
        Assert.Equal(0, git.Started);
        Assert.Equal(Card, stray.Card);
        Assert.Equal("src/other/b.txt", stray.Path);
        Assert.Equal(Head(), _table.Read().AuditedCommit);
    }

    [Fact]
    public void MM_14_Audit_MergeInTheRange_IsReadByGit_WithTheSameStray()
    {
        var from = Head();
        GitFixture.Git(_fx.Seed, "checkout", "-q", "-b", "side");
        File.WriteAllText(Path.Combine(_fx.Seed, "src", "other", "b.txt"), "side\n");
        GitFixture.Git(_fx.Seed, "commit", "-q", "-a", "-m", $"side\n\nCard: {Card}");
        GitFixture.Git(_fx.Seed, "checkout", "-q", "main");
        GitFixture.Git(_fx.Seed, "merge", "-q", "--no-ff", "-m", $"merge\n\nCard: {Card}", "side");
        GitFixture.Git(_fx.Seed, "push", "-q", "origin", "HEAD:main");
        var git = new GitRunner();
        var audit = Audit(git);
        Assert.Null(audit.ReadInProcess(from, Head()));
        var stray = Assert.Single(audit.Run(Head()));
        Assert.True(git.Started > 0);
        Assert.Equal("src/other/b.txt", stray.Path);
    }

    private AuditRunner Audit(GitRunner git) =>
        new(git, _fx.Seed, _table.Read, _table.Save, CardRequests.Folder(_log.Path), () => ["docs/known_gaps.md"], _clock, null);

    private string Head() => GitFixture.Git(_fx.Seed, "rev-parse", "HEAD");

    private static string Line(MainAuditCommit c) =>
        $"{c.Sha} [{c.Card}] own={c.Own} {string.Join(" ", c.Paths.Select(p => (p.New ? "+" : "") + p.Path))}";

    // What the audit took from git before 1065-B, read here the same way: sha, first Card trailer, paths with
    // their new-file mark, and the own-commit rule.
    private List<string> FromGit(string from, string head)
    {
        // git's bytes read as UTF-8, as the scheduler reads them (its console is UTF-8, ConsoleOutput).
        var output = System.Text.Encoding.UTF8.GetString(new GitRunner().RunBinary(_fx.Seed, [], "-c", "core.quotePath=false", "log", "--reverse",
            "--no-renames", "--name-status", "--format=%x1e%H%x1f%s%x1f%(trailers:key=Card,valueonly,separator=%x2c)%x1f", $"{from}..{head}")!);
        var lines = new List<string>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = record.Split('\x1f');
            var paths = f[3].Split('\n')
                .Select(l => l.TrimEnd('\r').Split('\t'))
                .Where(t => t.Length >= 2)
                .Select(t => (t[0].StartsWith('A') ? "+" : "") + t[^1])
                .ToList();
            var card = f[2].Split(',', '\n').Select(c => c.Trim()).FirstOrDefault(c => c.Length > 0) ?? "";
            var own = card.Length == 0 && f[1].StartsWith("jobs: ", StringComparison.Ordinal)
                && paths.All(p => p.TrimStart('+').StartsWith("jobs/", StringComparison.OrdinalIgnoreCase));
            lines.Add($"{f[0].Trim()} [{card}] own={own} {string.Join(" ", paths)}");
        }

        return lines;
    }
}
