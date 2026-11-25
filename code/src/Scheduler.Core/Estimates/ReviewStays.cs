// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Core.Estimates;

/// <summary>
/// One stay of a card in review (EST-15): when the commit that added <c>review/&lt;card&gt;.md</c> landed, when the
/// first later commit that deleted it landed (null while the stay is open), where the card went (done, queue,
/// active or gone, "" while open), and the two commits.
/// </summary>
public sealed record ReviewStay(
    string Card,
    DateTimeOffset Entered,
    DateTimeOffset? Left,
    string To,
    string EnteredCommit,
    string LeftCommit)
{
    /// <summary>Whether the stay has no end yet.</summary>
    public bool IsOpen => Left is null;
}

/// <summary>One line of Table 2 (EST-20): the closed stays of one kind (or category, or <c>all</c>).</summary>
public sealed record ReviewGroup(string Kind, int Stays, Figure Median, Figure P90, int ToDone, int ToQueue);

/// <summary>EST-20: how many stays are open, and the age in whole minutes of the oldest (null when none).</summary>
public sealed record ReviewOpen(int Count, int? OldestMinutes);

/// <summary>Table 2: its groups, <c>all</c> first, and the open stays beside them.</summary>
public sealed record ReviewTable(IReadOnlyList<ReviewGroup> Groups, ReviewOpen Open);

/// <summary>One project's closed stays and the kind of each of its cards (card id to kind, from <see cref="ReviewStays.KindsOf"/>).</summary>
public sealed record ReviewStaySource(IReadOnlyList<ReviewStay> Stays, IReadOnlyDictionary<string, string> Kinds);

/// <summary>
/// Time in review (EST-15 to EST-20), pure: the open stays of one project, kept as main's commits are applied
/// one by one, the line format of <c>review-stays.csv</c> (EST-16), and Table 2 (EST-20). No clock, no files,
/// no repository.
/// </summary>
public sealed class ReviewStays
{
    /// <summary>EST-16: the header of <c>review-stays.csv</c>.</summary>
    public const string Header = "entered,left,card,to,entered_commit,left_commit";

    /// <summary>EST-15: where a card went when the commit that took it out of review added it to none of the card folders.</summary>
    public const string Gone = "gone";

    /// <summary>EST-20: the key of the group of every stay.</summary>
    public const string All = "all";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";

    private static readonly string[] Destinations = ["done", "queue", "active"];

    private readonly string _jobsDir;
    private readonly Dictionary<string, ReviewStay> _open = new(StringComparer.Ordinal);

    /// <summary>Stays of the jobs folder <paramref name="jobsDir"/> (repository-relative, with <c>/</c>), none open yet.</summary>
    public ReviewStays(string jobsDir)
    {
        ArgumentNullException.ThrowIfNull(jobsDir);
        _jobsDir = jobsDir.Replace('\\', '/').Trim('/');
    }

    /// <summary>The open stays, oldest first.</summary>
    public IReadOnlyList<ReviewStay> Open => [.. _open.Values.OrderBy(s => s.Entered).ThenBy(s => s.Card, StringComparer.Ordinal)];

    /// <summary>
    /// EST-15: applies one commit of main's first-parent line. Each card deleted from review/ closes its open stay,
    /// going to the folder the same commit adds it to (else <see cref="Gone"/>); each card added to review/ opens a
    /// stay. Returns the stays the commit closed. A deletion with no open stay (history read from the middle) closes nothing.
    /// </summary>
    public IReadOnlyList<ReviewStay> Apply(MainCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        var closed = new List<ReviewStay>();
        foreach (var change in commit.Changes)
        {
            if (change.NewBlob is null && CardIn(change.Path, "review") is { } card && _open.Remove(card, out var stay))
            {
                closed.Add(stay with { Left = commit.Time, To = DestinationOf(commit, card), LeftCommit = commit.Sha });
            }
        }

        foreach (var change in commit.Changes)
        {
            if (change.OldBlob is null && change.NewBlob is not null && CardIn(change.Path, "review") is { } card)
            {
                _open[card] = new ReviewStay(card, commit.Time, null, "", commit.Sha, "");
            }
        }

        return closed;
    }

    /// <summary>Applies <paramref name="commits"/> in order, oldest first; returns every stay they closed, in the order they closed.</summary>
    public IReadOnlyList<ReviewStay> Apply(IEnumerable<MainCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);
        return [.. commits.SelectMany(Apply)];
    }

    /// <summary>Whether a path is one EST-17 reads: a file under the jobs folder's review/, done/, queue/ or active/.</summary>
    public bool Touches(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return CardIn(path, "review") is not null || Destinations.Any(d => CardIn(path, d) is not null);
    }

    /// <summary>EST-16: a closed stay as one line of <c>review-stays.csv</c>, its times local as ACCT-1 writes them.</summary>
    public static string Format(ReviewStay stay)
    {
        ArgumentNullException.ThrowIfNull(stay);
        return string.Join(',', LocalText(stay.Entered), stay.Left is { } left ? LocalText(left) : "", stay.Card, stay.To, stay.EnteredCommit, stay.LeftCommit);
    }

    /// <summary>EST-16: one line of <c>review-stays.csv</c> as a stay; null for the header, a blank line or a line that does not read.</summary>
    public static ReviewStay? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var f = line.Trim().Split(',');
        if (f.Length < 6 || f[2].Length == 0 || !TryLocal(f[0], out var entered))
        {
            return null;
        }

        DateTimeOffset? left = null;
        if (f[1].Length > 0)
        {
            if (!TryLocal(f[1], out var l))
            {
                return null;
            }

            left = l;
        }

        return new ReviewStay(f[2], entered, left, f[3], f[4], f[5]);
    }

    /// <summary>EST-20: each card's kind, from its newest accounting row that has one (rows are read in file order).</summary>
    public static IReadOnlyDictionary<string, string> KindsOf(IEnumerable<ParsedAccountingRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var kinds = new Dictionary<string, (DateTimeOffset At, string Kind)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var card = row["card"].Trim();
            var kind = row["kind"].Trim().ToLowerInvariant();
            if (card.Length == 0 || kind.Length == 0)
            {
                continue;
            }

            if (!kinds.TryGetValue(card, out var had) || row.Finished >= had.At)
            {
                kinds[card] = (row.Finished, kind);
            }
        }

        return kinds.ToDictionary(k => k.Key, k => k.Value.Kind, StringComparer.Ordinal);
    }

    /// <summary>
    /// EST-20: Table 2. Per kind (else <c>category:&lt;L&gt;</c> from the card's id) and for <c>all</c>: the closed stays that
    /// ended within the window, the newest 100 (EST-9), their median and 90th percentile in whole minutes with the pool's
    /// fallback (EST-25; never for a category group, EST-26), and how many went to done and to queue. A stay that ends
    /// before it starts is skipped. Open stays are only counted, in <see cref="ReviewTable.Open"/>. A null
    /// <paramref name="pool"/> is a scheduler run for one project: no figure is the pool's.
    /// </summary>
    public static ReviewTable Table(
        ReviewStaySource project,
        IReadOnlyList<ReviewStaySource>? pool,
        IReadOnlyList<ReviewStay> open,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(open);

        var own = Groups([project], now);
        var pooled = pool is null ? null : Groups(pool, now);
        if (!own.ContainsKey(All))
        {
            own[All] = [];
        }

        var groups = new List<ReviewGroup>(own.Count);
        foreach (var (key, stays) in own)
        {
            List<int>? fallback = null;
            if (!key.StartsWith(EstimateFigures.CategoryPrefix, StringComparison.Ordinal) && pooled is not null
                && pooled.TryGetValue(key, out var poolStays))
            {
                fallback = [.. poolStays.Select(Minutes)];
            }

            var minutes = stays.Select(Minutes).ToList();
            groups.Add(new ReviewGroup(
                key,
                stays.Count,
                EstimateFigures.Median(minutes, fallback),
                EstimateFigures.P90(minutes, fallback),
                stays.Count(s => s.To == "done"),
                stays.Count(s => s.To == "queue")));
        }

        var oldest = open.Where(s => s.IsOpen).Select(s => (DateTimeOffset?)s.Entered).Min();
        return new ReviewTable(
            [.. groups
                .OrderBy(g => g.Kind == All ? 0 : g.Kind.StartsWith(EstimateFigures.CategoryPrefix, StringComparison.Ordinal) ? 2 : 1)
                .ThenBy(g => g.Kind, Text.SortComparer)],
            new ReviewOpen(open.Count(s => s.IsOpen), oldest is { } o ? AccountingRow.MinutesOf(now - o) : null));
    }

    /// <summary>EST-20: the group of a card with no kind, from the letter in its id (<c>&lt;n&gt;-&lt;L&gt;-&lt;slug&gt;</c>).</summary>
    public static string CategoryKey(string card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var parts = card.Split('-');
        var letter = parts.Length > 2 && parts[1].Length > 0 ? parts[1] : "unknown";
        return EstimateFigures.CategoryPrefix + letter;
    }

    // Key -> the window's closed stays of every source, newest first (EST-9), "all" included.
    private static Dictionary<string, List<ReviewStay>> Groups(IEnumerable<ReviewStaySource> sources, DateTimeOffset now)
    {
        var byKey = new Dictionary<string, List<ReviewStay>>(Text.Comparer);
        foreach (var source in sources)
        {
            foreach (var stay in source.Stays)
            {
                if (stay.Left is not { } left || left < stay.Entered)
                {
                    continue;
                }

                var key = source.Kinds.TryGetValue(stay.Card, out var kind) && kind.Length > 0 ? kind : CategoryKey(stay.Card);
                foreach (var k in new[] { key, All })
                {
                    if (!byKey.TryGetValue(k, out var list))
                    {
                        list = [];
                        byKey[k] = list;
                    }

                    list.Add(stay);
                }
            }
        }

        var windowed = new Dictionary<string, List<ReviewStay>>(Text.Comparer);
        foreach (var (key, list) in byKey)
        {
            var kept = EstimateFigures.Window(list, s => s.Left!.Value, now);
            if (kept.Count > 0)
            {
                windowed[key] = [.. kept];
            }
        }

        return windowed;
    }

    private static int Minutes(ReviewStay stay) => AccountingRow.MinutesOf(stay.Left!.Value - stay.Entered);

    private static string LocalText(DateTimeOffset time) => time.ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static bool TryLocal(string text, out DateTimeOffset time)
    {
        if (DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
        {
            time = new DateTimeOffset(local);
            return true;
        }

        time = default;
        return false;
    }

    // Where the commit that took <card> out of review put it: the first card folder it adds the card to, else gone.
    private string DestinationOf(MainCommit commit, string card) =>
        Destinations.FirstOrDefault(d => commit.Changes.Any(c => c.OldBlob is null && c.NewBlob is not null && CardIn(c.Path, d) == card)) ?? Gone;

    // The card id (file name less .md) of a path directly in <jobsDir>/<folder>/, else null.
    private string? CardIn(string path, string folder)
    {
        var prefix = _jobsDir.Length == 0 ? folder + "/" : _jobsDir + "/" + folder + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".md", StringComparison.Ordinal))
        {
            return null;
        }

        var name = path[prefix.Length..^3];
        return name.Length == 0 || name.Contains('/', StringComparison.Ordinal) ? null : name;
    }
}
