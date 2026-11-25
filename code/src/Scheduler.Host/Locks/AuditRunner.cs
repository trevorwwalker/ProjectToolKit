// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Core.Moves;
using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Locks;

/// <summary>
/// Dynamic locks, the audit of main (design <c>dynamic-locks.md</c>, DL-19; card 1021-B). Run on every read of
/// main the card reader makes (<see cref="CardReader.MainSeen"/>): when main has moved past the table's last
/// audited commit, it reads the new commits (in process since 1065-B, else <c>git log --name-status</c>; each with its <c>Card:</c> trailer), checks
/// them with <see cref="MainAudit"/>, keeps the new last audited commit in the table and logs each stray. A card
/// with a stray keeps it, in a file beside the lock table too, so a restart still knows it (1216-B): its waiting finish request in the moves folder, To: done,
/// is turned into To: review (<see cref="CardMoves.Stray"/>) before the move step applies it. It never reverts
/// anything.
/// </summary>
public sealed class AuditRunner
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly GitRunner _git;
    private readonly string _repo;
    private readonly Func<LockTable> _read;
    private readonly Action<LockTable> _save;
    private readonly string _moves;
    private readonly Func<IReadOnlyList<string>?> _addOnly;
    private readonly IClock _clock;
    private readonly ISchedulerLog? _log;
    private readonly Dictionary<string, string> _strays = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _straysFile;
    private MainObjects? _objects;
    private bool _opened;
    private bool _waitLogged;

    /// <summary>The most strays kept in the file: the newest ones.</summary>
    public const int MaxStrays = 500;

    /// <summary>The audit of <paramref name="repo"/>'s main, kept in the table <paramref name="read"/> and <paramref name="save"/> give.</summary>
    /// <param name="git">Runs git.</param>
    /// <param name="repo">The repository (any worktree of it that has fetched main).</param>
    /// <param name="read">Reads the lock table.</param>
    /// <param name="save">Writes the lock table.</param>
    /// <param name="moves">The moves folder, where finish requests wait.</param>
    /// <param name="addOnly">categories.json's shared add-only paths (DL-15 a); null while they are not known, and the audit waits (1274-B).</param>
    /// <param name="clock">The clock: a commit lands when the scheduler sees it.</param>
    /// <param name="log">The scheduler's log.</param>
    public AuditRunner(GitRunner git, string repo, Func<LockTable> read, Action<LockTable> save, string moves,
        Func<IReadOnlyList<string>?> addOnly, IClock clock, ISchedulerLog? log)
    {
        _git = git;
        _repo = repo;
        _read = read;
        _save = save;
        _moves = moves;
        _addOnly = addOnly;
        _clock = clock;
        _log = log;
        var logDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(moves)) ?? "";
        _straysFile = Path.ChangeExtension(Scheduler.Core.Locks.LockTableText.PathIn(logDir, repo), ".strays.log");
        Load();
    }

    /// <summary>
    /// The add-only paths (DL-15 a) the audit checks with (1274-B): the registry's when the tick has read
    /// categories.json (<paramref name="current"/>), else categories.json read from main now
    /// (<paramref name="readCategories"/>), because the tick reads main's cards, and so runs the audit, before it
    /// reads the categories: on the first tick after a start the registry has none yet. No categories file on main
    /// (or one with no categories) has no add-only paths, as the registry reads it. Null when main's file is not
    /// valid JSON, so the list cannot be known: the audit then waits.
    /// </summary>
    public static IReadOnlyList<string>? AddOnlyOf(CategoriesFile? current, Func<string?> readCategories)
    {
        if (current is not null)
        {
            return current.AddOnly;
        }

        var text = readCategories();
        if (Scheduler.Core.Categories.CategoriesEdit.Parse(text) is { } file)
        {
            return file.AddOnly;
        }

        try
        {
            using var _ = string.IsNullOrWhiteSpace(text) ? null : System.Text.Json.JsonDocument.Parse(text);
            return [];
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The file the strays are kept in, beside the lock table (1216-B): one <c>card TAB path</c> per line, oldest first.</summary>
    public string StraysPath => _straysFile;

    private void Load()
    {
        try
        {
            if (!File.Exists(_straysFile))
            {
                return;
            }

            var bad = 0;
            foreach (var line in File.ReadAllLines(_straysFile, Utf8))
            {
                var t = line.Split('	');
                if (t.Length == 2 && t[0].Length > 0)
                {
                    _strays[t[0]] = t[1];
                }
                else if (line.Trim().Length > 0)
                {
                    bad++;
                }
            }

            if (bad > 0)
            {
                _log?.Write($"locks: the strays file {_straysFile} has {bad} unreadable line(s); they are ignored");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _strays.Clear();
            _log?.Write($"locks: the strays file {_straysFile} could not be read ({e.Message}); starting with none");
        }
    }

    private void Persist()
    {
        try
        {
            var rows = _strays.Select(kv => $"{kv.Key}	{kv.Value}").ToList();
            if (rows.Count > MaxStrays)
            {
                rows = rows.GetRange(rows.Count - MaxStrays, MaxStrays);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_straysFile)!);
            File.WriteAllLines(_straysFile, rows, Utf8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Write($"locks: the strays file {_straysFile} could not be written ({e.Message})");
        }
    }

    /// <summary>The first stray path of each card found this run ("" for a commit with no card).</summary>
    public IReadOnlyDictionary<string, string> Strays => _strays;

    /// <summary>
    /// Audits the commits up to <paramref name="head"/> (main's commit now), then turns any waiting To: done of
    /// a card with a stray into To: review. Returns the strays found by this call.
    /// </summary>
    public IReadOnlyList<MainAuditStray> Run(string head)
    {
        var found = Audit(head);
        if (_strays.Count > 0)
        {
            ReviewFinishes();
        }

        return found;
    }

    private IReadOnlyList<MainAuditStray> Audit(string head)
    {
        if (string.IsNullOrWhiteSpace(head))
        {
            return [];
        }

        var table = _read();
        var from = table.AuditedCommit;
        if (string.Equals(from, head, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (from.Length == 0)
        {
            _save(table with { AuditedCommit = head });
            _log?.Write($"locks: audit of main starts at {Short(head)}");
            return [];
        }

        // 1274-B: never with an empty list standing for one not read yet (a version bump would be a stray), and
        // never skipping: the last audited commit stays, and a later run audits the same commits.
        var addOnly = _addOnly();
        if (addOnly is null)
        {
            if (!_waitLogged)
            {
                _log?.Write($"locks: audit of main waits at {Short(from)}: the shared add-only files are not known yet");
                _waitLogged = true;
            }

            return [];
        }

        _waitLogged = false;

        // 1065-B (MM-14): the new commits are read in process; git only when the reader cannot answer exactly.
        var commits = ReadInProcess(from, head);
        if (commits is null)
        {
            var log = _git.Run(_repo, "-c", "core.quotePath=false", "log", "--reverse", "--no-renames", "--name-status",
                "--format=%x1e%H%x1f%s%x1f%(trailers:key=Card,valueonly,separator=%x2c)%x1f", $"{from}..{head}");
            if (!log.Ok)
            {
                // The last audited commit is gone (main rewritten) or git failed: start again at main's commit.
                _save(_read() with { AuditedCommit = head });
                _log?.Write($"locks: audit of main could not read {Short(from)}..{Short(head)} ({log.Stderr.Trim()}); it starts again at {Short(head)}");
                return [];
            }

            commits = Parse(log.Stdout, _clock.Now);
        }

        var result = MainAudit.Check(table, commits, addOnly);
        _save(_read() with { AuditedCommit = result.Table.AuditedCommit });
        var added = false;
        foreach (var s in result.Strays)
        {
            _log?.Write(s.Card.Length > 0
                ? $"locks: stray {Short(s.Sha)} of {s.Card}: {s.Reason}"
                : $"locks: stray {Short(s.Sha)}: {s.Reason}");
            if (s.Card.Length > 0 && !_strays.ContainsKey(s.Card))
            {
                _strays[s.Card] = s.Path;
                added = true;
            }
        }

        if (added)
        {
            while (_strays.Count > MaxStrays)
            {
                _strays.Remove(_strays.Keys.First());
            }

            Persist();
        }

        return result.Strays;
    }

    /// <summary>
    /// The commits in <c>git log --format=%x1e%H%x1f%s%x1f%(trailers...)%x1f --name-status</c> output. The
    /// scheduler's own commits (no <c>Card:</c> trailer, a <c>jobs: </c> subject, only paths under <c>jobs/</c>)
    /// are marked <see cref="MainAuditCommit.Own"/>.
    /// </summary>
    internal static IReadOnlyList<MainAuditCommit> Parse(string output, DateTimeOffset landed)
    {
        var commits = new List<MainAuditCommit>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = record.Split('\x1f');
            if (f.Length < 4 || f[0].Trim().Length == 0)
            {
                continue;
            }

            var paths = new List<PushCheckPath>();
            foreach (var line in f[3].Split('\n'))
            {
                var t = line.TrimEnd('\r').Split('\t');
                if (t.Length >= 2 && t[0].Length > 0 && t[^1].Length > 0)
                {
                    paths.Add(new PushCheckPath(t[^1], New: t[0].StartsWith('A')));
                }
            }

            commits.Add(Commit(f[0].Trim(), f[1], f[2], paths, landed));
        }

        return commits;
    }

    /// <summary>
    /// The commits after <paramref name="from"/> up to <paramref name="head"/> read in process (MM-14), the same as
    /// <see cref="Parse"/> gives from git's log; null when git must be asked: no reader for the repository (MM-2),
    /// an object it cannot read, or a range that is not one first-parent line (a merge, a rewritten main, a range
    /// longer than <see cref="InProcessHistory.WalkLimit"/>).
    /// </summary>
    public IReadOnlyList<MainAuditCommit>? ReadInProcess(string from, string head)
    {
        if (!_opened)
        {
            _objects = InProcessHistory.Open(_repo);
            _opened = true;
        }

        if (_objects is null || !ObjectId.TryParse(from, out var f) || !ObjectId.TryParse(head, out var h))
        {
            return null;
        }

        try
        {
            using var s = _objects.Begin();
            var range = InProcessHistory.FirstParentRange(s, f, h);
            if (range is null)
            {
                return null;
            }

            var commits = new List<MainAuditCommit>();
            ObjectId? parentTree = range.Count > 0 ? s.ReadCommit(f).Tree : null;
            foreach (var c in range)
            {
                var paths = InProcessHistory.Diff(s, parentTree, c.Commit.Tree)
                    .Select(p => new PushCheckPath(p.Path, New: p.Status == 'A'))
                    .ToList();
                var cards = string.Join(',', InProcessHistory.Trailers(c.Commit.Message, "Card"));
                commits.Add(Commit(c.Id.ToString(), c.Commit.Subject, cards, paths, _clock.Now));
                parentTree = c.Commit.Tree;
            }

            return commits;
        }
        catch (Exception e) when (InProcessHistory.IsReadFailure(e))
        {
            return null;
        }
    }

    // One commit as the audit sees it. The scheduler's own commits (no Card trailer, a "jobs: " subject, only paths
    // under jobs/) are marked Own.
    private static MainAuditCommit Commit(string sha, string subject, string cards, List<PushCheckPath> paths, DateTimeOffset landed)
    {
        var card = cards.Split(',', '\n').Select(c => c.Trim()).FirstOrDefault(c => c.Length > 0) ?? "";
        var own = card.Length == 0
            && subject.StartsWith("jobs: ", StringComparison.Ordinal)
            && paths.All(p => p.Path.StartsWith("jobs/", StringComparison.OrdinalIgnoreCase));
        return new MainAuditCommit { Sha = sha, Card = card, Paths = paths, Own = own, Landed = landed };
    }

    // DL-19: a card with a stray does not reach done unseen. Its waiting To: done request is rewritten in place.
    private void ReviewFinishes()
    {
        if (!Directory.Exists(_moves))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_moves, "*" + CardRequests.Extension))
        {
            try
            {
                var name = Path.GetFileName(file);
                var request = CardRequests.Parse(name, File.ReadAllText(file, Utf8));
                if (!request.Ok || request.To != CardFolder.Done || !_strays.TryGetValue(request.Card, out var path))
                {
                    continue;
                }

                var review = CardMoves.Stray(request, path);
                File.WriteAllText(file + ".tmp", Text(review), Utf8);
                File.Move(file + ".tmp", file, overwrite: true);
                _log?.Write($"locks: {request.Card}'s finish goes to review: it changed {path} without its lock");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // being written or applied: the next read tries again
            }
        }
    }

    /// <summary>A move request as its file text (the form <see cref="CardRequests.Parse"/> reads).</summary>
    internal static string Text(CardRequest r)
    {
        var b = new StringBuilder();
        b.Append("Card: ").Append(r.Card).Append('\n');
        if (r.From != CardFolder.Active)
        {
            b.Append("From: ").Append(Folder(r.From)).Append('\n');
        }

        b.Append("To: ").Append(Folder(r.To)).Append('\n');
        if (r.Slot is { } slot)
        {
            b.Append("Slot: ").Append(slot.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }

        if (r.Status.Length > 0)
        {
            b.Append("Status: ").Append(r.Status).Append('\n');
        }

        if (r.Claim)
        {
            b.Append("Claim: yes\n");
        }

        if (r.Text is { } text)
        {
            b.Append("---\n").Append(text);
        }

        return b.ToString();
    }

    private static string Folder(CardFolder f) => f.ToString().ToLowerInvariant();

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;
}
