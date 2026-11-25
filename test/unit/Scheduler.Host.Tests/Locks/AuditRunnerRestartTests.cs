// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Categories;
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
/// 1274-B, design <c>dynamic-locks.md</c> DL-19: the first audit after a start. The tick reads main's cards (and so
/// runs the audit) before it reads categories.json, so the add-only list (DL-15 a) is not known from the registry
/// yet; a card's version bump of a shared add-only file must still not be a stray, and an unheld path still is.
/// </summary>
[ParallelMethods]
public sealed class AuditRunnerRestartTests : IDisposable
{
    private const string Card = "7-B-adopted";
    private const string Repo = "C:/Projects/Restarted";
    private const string Props = "code/src/Directory.Build.props";
    private const string Categories = """
        { "lanes": { "B": { "name": "host", "owns": ["src/held/"] } },
          "shared": { "addOnly": [".gitignore", "code/src/Directory.Build.props"] } }
        """;

    private readonly GitFixture _fx = new();
    private readonly TempDir _log = new();
    private readonly FakeClock _clock = new(DateTimeOffset.Now);
    private readonly ListLog _lines = new();
    private readonly LockTableFile _table;
    private readonly string _moves;
    private CategoriesFile? _current;   // the registry's categories: null until the tick has read them
    private string? _onMain = Categories;   // categories.json on main as the host reads it

    public AuditRunnerRestartTests()
    {
        _fx.Push("seed", new Dictionary<string, string?>
        {
            ["src/held/a.txt"] = "1\n", ["src/other/b.txt"] = "1\n", [Props] = "<Version>1.0.0</Version>\n",
        });

        // An adopted card: its worker kept running across the restart, its entry in the table holds its scope and
        // the table's last audited commit is behind main.
        _table = new LockTableFile(_log.Path, Repo, null);
        _table.Save(LockTable.Empty(Repo).Start(Card, new CardAge(1, 1), Scope.Of("src/held/"), _clock.Now.AddMinutes(-5), "p", 1)
            with { AuditedCommit = Head() });
        _moves = CardRequests.Folder(_log.Path);
    }

    // The runner as ProjectParts composes it after a start.
    private AuditRunner Started() =>
        new(new GitRunner(), _fx.Seed, _table.Read, _table.Save, _moves, () => AuditRunner.AddOnlyOf(_current, () => _onMain), _clock, _lines);

    private void Bump() =>
        _fx.Push($"Rev 1.0.1\n\nCard: {Card}", new Dictionary<string, string?> { [Props] = "<Version>1.0.1</Version>\n" });

    [Fact]
    public void First_run_after_a_start_finds_no_stray_in_a_version_bump()
    {
        Bump();
        Assert.Empty(Started().Run(Head()));
        Assert.Equal(Head(), _table.Read().AuditedCommit);
        Assert.DoesNotContain(_lines.Lines, l => l.Contains("stray", StringComparison.Ordinal));
    }

    [Fact]
    public void First_run_with_the_categories_already_read_finds_no_stray_in_a_version_bump()
    {
        _current = CategoriesEdit.Parse(Categories);
        _onMain = null;
        Bump();
        Assert.Empty(Started().Run(Head()));
        Assert.Equal(Head(), _table.Read().AuditedCommit);
    }

    [Fact]
    public void Categories_not_known_yet_the_audit_waits_and_keeps_its_place()
    {
        var from = Head();
        _onMain = "{ not json";
        Bump();
        var audit = Started();

        Assert.Empty(audit.Run(Head()));
        Assert.Equal(from, _table.Read().AuditedCommit);   // not skipped: the commit is audited when they are known

        _onMain = Categories;
        Assert.Empty(audit.Run(Head()));
        Assert.Equal(Head(), _table.Read().AuditedCommit);
        Assert.Empty(audit.Strays);
    }

    [Fact]
    public void Waiting_for_the_categories_still_finds_an_unheld_path_once_they_are_known()
    {
        var from = Head();
        _onMain = "{ not json";
        _fx.Push($"change b\n\nCard: {Card}", new Dictionary<string, string?> { ["src/other/b.txt"] = "2\n" });
        var audit = Started();

        Assert.Empty(audit.Run(Head()));
        Assert.Equal(from, _table.Read().AuditedCommit);

        _current = CategoriesEdit.Parse(Categories);
        var stray = Assert.Single(audit.Run(Head()));
        Assert.Equal("src/other/b.txt", stray.Path);
    }

    [Fact]
    public void First_run_after_a_start_still_finds_an_unheld_path_that_is_not_add_only()
    {
        _fx.Push($"Rev 1.0.1 and b\n\nCard: {Card}", new Dictionary<string, string?>
        {
            [Props] = "<Version>1.0.1</Version>\n", ["src/other/b.txt"] = "2\n",
        });
        var stray = Assert.Single(Started().Run(Head()));
        Assert.Equal(Card, stray.Card);
        Assert.Equal("src/other/b.txt", stray.Path);
        Assert.Equal("src/other/b.txt", Assert.Single(Started().Strays).Value);
    }

    [Fact]
    public void Add_only_of_reads_the_registry_first_then_main()
    {
        Assert.Equal([".gitignore", Props], AuditRunner.AddOnlyOf(CategoriesEdit.Parse(Categories), () => throw new InvalidOperationException()));
        Assert.Equal([".gitignore", Props], AuditRunner.AddOnlyOf(null, () => Categories));
        Assert.Empty(AuditRunner.AddOnlyOf(null, () => null)!);   // no categories file on main: nothing is add-only
        Assert.Empty(AuditRunner.AddOnlyOf(null, () => "{}")!);
        Assert.Null(AuditRunner.AddOnlyOf(null, () => "{ not json"));
    }

    public void Dispose()
    {
        _fx.Dispose();
        _log.Dispose();
    }

    private string Head() => GitFixture.Git(_fx.Seed, "rev-parse", "HEAD");
}
