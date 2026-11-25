// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Locks;
using Scheduler.Core.Moves;
using Scheduler.Core.Scopes;
using Scheduler.Host.Locks;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1020-B: a temporary log folder with a lock table of two running cards - 100-A-old (slot 1, holds
/// src/a/) and the younger 200-B-young (slot 2, holds src/b/) - the lock step over its moves folder, and
/// the releases, so tests can play the scheduler's side of a lock request.
/// </summary>
internal sealed class LockRig : IDisposable
{
    public const string Old = "100-A-old";
    public const string Young = "200-B-young";
    public const string Repo = "C:/Projects/Repo";

    private readonly TempDir _dir = new();

    public LockRig()
    {
        LogDir = Path.Combine(_dir.Path, "log");
        Directory.CreateDirectory(LogDir);
        Table = new LockTableFile(LogDir, Repo, Log);
        Step = new LockRequestStep(Moves, Table.Read, Table.Save, Clock, Log);
        Releases = new LockReleases(Table, c => Running.Contains(c), c => PendingMoves.Contains(c), Log);
        Table.Save(LockTable.Empty(Repo)
            .Start(Old, new CardAge(1, 100), Scope.Of("src/a/"), Clock.Now, "p", 1)
            .Start(Young, new CardAge(1, 200), Scope.Of("src/b/"), Clock.Now, "p", 2));
    }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 21, 4, 0, 0, TimeSpan.Zero));

    public ListLog Log { get; } = new();

    public string LogDir { get; }

    public string Moves => CardRequests.Folder(LogDir);

    public LockTableFile Table { get; }

    public LockRequestStep Step { get; }

    public LockReleases Releases { get; }

    public HashSet<string> Running { get; } = [Old, Young];

    public HashSet<string> PendingMoves { get; } = [];

    public LockRules Rules { get; } = new();

    /// <summary>Slot 1 runs the old card, slot 2 the young one.</summary>
    public static string? SlotCard(int slot) => slot switch { 1 => Old, 2 => Young, _ => null };

    /// <summary>Writes a request as the lock command would; returns its file name.</summary>
    public string Ask(string card, int slot, params string[] paths)
    {
        Directory.CreateDirectory(Moves);
        var name = LockRequests.FileName(Clock.Now, slot);
        File.WriteAllText(Path.Combine(Moves, name), LockRequests.Text(card, slot, paths, "test"));
        return name;
    }

    /// <summary>One lock step, as the tick runs it.</summary>
    public IReadOnlyList<LockDecision> RunStep() => Step.Run(Rules, SlotCard, _ => null);

    /// <summary>The reply written for a request, or "".</summary>
    public string Reply(string name) =>
        File.Exists(Step.ReplyPath(name)) ? File.ReadAllText(Step.ReplyPath(name)).Trim() : "";

    public CardLocks Row(string card) => Table.Read().Find(card) ?? throw new InvalidOperationException(card + " has no row");

    public void Dispose() => _dir.Dispose();
}
