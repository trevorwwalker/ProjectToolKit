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
/// 1021-B, design <c>dynamic-locks.md</c> DL-19 (section 7, Audit) on a temp repository: commits since the
/// table's last audited one, checked against the held set their Card trailer names.
/// </summary>
[ParallelMethods]
public sealed class AuditRunnerTests : IDisposable
{
    private const string Card = "1-B-audit";
    private const string Repo = "C:/Projects/Audited";
    private readonly GitFixture _fx = new();
    private readonly TempDir _log = new();
    private readonly FakeClock _clock = new(DateTimeOffset.Now);
    private readonly ListLog _lines = new();
    private readonly LockTableFile _table;
    private readonly AuditRunner _audit;
    private readonly string _moves;

    public AuditRunnerTests()
    {
        _fx.Push("seed", new Dictionary<string, string?> { ["src/held/a.txt"] = "1\n", ["src/other/b.txt"] = "1\n" });
        _table = new LockTableFile(_log.Path, Repo, null);
        _table.Save(LockTable.Empty(Repo).Start(Card, new CardAge(1, 1), Scope.Of("src/held/"), _clock.Now.AddMinutes(-5), "p", 1)
            with { AuditedCommit = Head() });
        _moves = CardRequests.Folder(_log.Path);
        _audit = new AuditRunner(new GitRunner(), _fx.Seed, _table.Read, _table.Save, _moves, () => ["docs/known_gaps.md"], _clock, _lines);
    }

    [Fact]
    public void Commit_with_its_trailer_and_held_paths_is_clean()
    {
        _fx.Push($"change a\n\nCard: {Card}", new Dictionary<string, string?> { ["src/held/a.txt"] = "2\n", ["docs/known_gaps.md"] = "- gap\n" });
        Assert.Empty(_audit.Run(Head()));
        Assert.Equal(Head(), _table.Read().AuditedCommit);
    }

    [Fact]
    public void Unheld_path_is_a_stray_and_logged()
    {
        _fx.Push($"change b\n\nCard: {Card}", new Dictionary<string, string?> { ["src/other/b.txt"] = "2\n" });
        var stray = Assert.Single(_audit.Run(Head()));
        Assert.Equal(Card, stray.Card);
        Assert.Equal("src/other/b.txt", stray.Path);
        Assert.Contains(_lines.Lines, l => l.Contains($"stray", StringComparison.Ordinal) && l.Contains("src/other/b.txt", StringComparison.Ordinal));
        Assert.Equal(Head(), _table.Read().AuditedCommit);
    }

    [Fact]
    public void No_trailer_while_workers_run_is_a_stray_but_the_schedulers_own_commits_are_not()
    {
        _fx.Push($"jobs: done {Card}", new Dictionary<string, string?> { [$"jobs/done/{Card}.md"] = "# done\n" });
        _fx.Push("by hand", new Dictionary<string, string?> { ["src/other/b.txt"] = "3\n" });
        var stray = Assert.Single(_audit.Run(Head()));
        Assert.Equal("", stray.Card);
    }

    [Fact]
    public void First_run_starts_at_mains_commit()
    {
        _table.Save(_table.Read() with { AuditedCommit = "" });
        _fx.Push($"change b\n\nCard: {Card}", new Dictionary<string, string?> { ["src/other/b.txt"] = "2\n" });
        Assert.Empty(_audit.Run(Head()));
        Assert.Equal(Head(), _table.Read().AuditedCommit);
    }

    [Fact]
    public void A_strays_waiting_finish_goes_to_review()
    {
        Directory.CreateDirectory(_moves);
        var file = Path.Combine(_moves, "20261121-050000-000-slot1" + CardRequests.Extension);
        File.WriteAllText(file, $"Card: {Card}\nTo: done\n---\n# {Card}: t\n\nCategory: B\nStatus: done\n\n## Result\n\nok\n");
        _fx.Push($"change b\n\nCard: {Card}", new Dictionary<string, string?> { ["src/other/b.txt"] = "2\n" });

        _audit.Run(Head());

        var request = CardRequests.Parse(Path.GetFileName(file), File.ReadAllText(file));
        Assert.True(request.Ok, request.Error);
        Assert.Equal(CardFolder.Review, request.To);
        Assert.Contains("Status: review: changed src/other/b.txt without its lock", request.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_reader_hands_main_to_the_audit_on_every_read()
    {
        var seen = new List<string>();
        var reader = new CardReader(_fx.Main(), "jobs", _lines) { ByCommit = true, MainSeen = seen.Add };
        GitFixture.Git(_fx.Repo, "fetch", "-q", "origin");
        reader.Read();
        reader.Read();
        Assert.Equal([Head(), Head()], seen);
    }

    public void Dispose()
    {
        _fx.Dispose();
        _log.Dispose();
    }

    private AuditRunner Again() =>
        new(new GitRunner(), _fx.Seed, _table.Read, _table.Save, _moves, () => ["docs/known_gaps.md"], _clock, _lines);

    private void PushStray() =>
        _fx.Push($"change b\n\nCard: {Card}", new Dictionary<string, string?> { ["src/other/b.txt"] = Guid.NewGuid().ToString() });

    [Fact]
    public void Strays_survive_a_new_runner_over_the_same_folder()
    {
        PushStray();
        _audit.Run(Head());
        var again = Again();
        Assert.Equal("src/other/b.txt", Assert.Single(again.Strays).Value);
        Assert.Equal(Card, Assert.Single(again.Strays).Key);
    }

    [Fact]
    public void The_strays_file_keeps_only_the_newest_entries()
    {
        File.WriteAllLines(_audit.StraysPath, Enumerable.Range(0, AuditRunner.MaxStrays).Select(i => $"old-{i}	p{i}"));
        var again = Again();
        PushStray();
        again.Run(Head());
        var lines = File.ReadAllLines(again.StraysPath);
        Assert.Equal(AuditRunner.MaxStrays, lines.Length);
        Assert.DoesNotContain(lines, l => l.StartsWith("old-0	", StringComparison.Ordinal));
        Assert.StartsWith(Card + "	", lines[^1], StringComparison.Ordinal);
        Assert.Equal(AuditRunner.MaxStrays, Again().Strays.Count);
    }

    [Fact]
    public void A_missing_file_gives_none()
    {
        Assert.Empty(Again().Strays);
    }

    [Fact]
    public void A_broken_file_gives_none_and_a_log_line()
    {
        File.WriteAllText(_audit.StraysPath, "not a stray list{{{");
        var again = Again();
        Assert.Empty(again.Strays);
        Assert.Contains(_lines.Lines, l => l.Contains("strays file", StringComparison.Ordinal));
    }

    private string Head() => GitFixture.Git(_fx.Seed, "rev-parse", "HEAD");
}
