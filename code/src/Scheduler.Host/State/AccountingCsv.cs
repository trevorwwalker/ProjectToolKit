// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.State;

/// <summary>
/// accounting.csv in the log folder (ACCT-4, ACCT-5). Host owns the file; the row that goes in it
/// is Core's <see cref="AccountingRow"/>. At start-up a header from before the model column is
/// rewritten in place and the rows of the last 48 hours become the figures
/// (<c>Seed-RunStats</c> L863), and the last ten runs become the page's Recent events with the
/// log that belongs to each (<c>Seed-Recent</c> L812).
/// </summary>
public sealed class AccountingCsv(string logDir, IClock clock, ISchedulerLog? log = null)
{
    /// <summary>WT-P6: Core's header and one more column at the end, the worker's type.</summary>
    public static readonly string Header = AccountingRow.Header + ",type" + AccountingRow.CardColumns;

    /// <summary>The rows read back into the figures reach this far behind the start (ACCT-4).</summary>
    private static readonly TimeSpan Window = TimeSpan.FromHours(48);

    /// <summary>The readable logs are named <c>&lt;card id&gt;-&lt;start&gt;.log</c> (WRK-9).</summary>
    private static readonly Regex LogName = new(@"^(\d+-[A-Z])-(\d{8}-\d{6})\.log$", RegexOptions.CultureInvariant);

    /// <summary>The card id a row's card name begins with (ACCT-5).</summary>
    private static readonly Regex CardId = new(@"^(\d+-[A-Z])", RegexOptions.CultureInvariant);

    private readonly List<RecentEvent> _recent = [];

    private readonly List<RunStat> _runs = [];

    private readonly object _trendLock = new();
    // 1118-B: the last error logged for each read, so one that repeats every tick is logged once.
    private readonly Dictionary<string, string> _told = new(StringComparer.Ordinal);

    // The lines of the last read and the file's stamp then (770-B), so the page rebuilds that find
    // the file untouched cost nothing.
    private (DateTime Written, IReadOnlyList<RunStat> Runs)? _trend;

    /// <summary>The recent events read so far, newest first.</summary>
    public IReadOnlyList<RecentEvent> Recent => _recent;

    /// <summary>
    /// The runs of the last 48 hours for the Control window's figures (ACCT-6): those
    /// <see cref="SeedRunStats"/> read at start, then every run <see cref="Add"/> records.
    /// </summary>
    public IReadOnlyList<RunStat> Runs => _runs;

    /// <summary>The file's path in the log folder.</summary>
    public string FilePath { get; } = System.IO.Path.Combine(logDir, "accounting.csv");

    /// <summary>
    /// <c>Account</c> (L844) and the recent-list step after a finish (TIME-20): append the row
    /// (the header first in a new file), and put the run first in the recent list, which keeps 10.
    /// </summary>
    public void Add(AccountingRow row, string? view, string type = "")
    {
        Directory.CreateDirectory(logDir);
        if (!File.Exists(FilePath))
        {
            File.AppendAllText(FilePath, Header + Environment.NewLine);
        }
        File.AppendAllText(FilePath, row.ToCsvLine() + "," + type + row.CardFields() + Environment.NewLine);
        _recent.Insert(0, new RecentEvent
        {
            When = row.Finished,
            Slot = row.Slot,
            Type = type,
            Model = row.Model,
            Card = row.Card,
            Result = row.Result,
            Minutes = row.Minutes,
            Log = view,
        });
        while (_recent.Count > 10)
        {
            _recent.RemoveAt(10);
        }

        // The figures (ACCT-6) take every finished run; those older than the window are dropped.
        _runs.Add(new RunStat
        {
            Finished = row.Finished,
            Result = row.Result,
            Minutes = row.Minutes,
            Model = row.Model,
            Card = IdOf(row.Card),
        });
        _runs.RemoveAll(r => r.Finished < clock.Now - Window);
    }

    /// <summary>
    /// <c>Seed-RunStats</c> (L863): rewrite a header that has no model column, then read the rows
    /// of the last 48 hours into the figures. Old rows have no model, so they come back with none.
    /// </summary>
    public IReadOnlyList<RunStat> SeedRunStats()
    {
        var stats = new List<RunStat>();
        try
        {
            if (!File.Exists(FilePath))
            {
                return stats;
            }

            var lines = File.ReadAllLines(FilePath).ToList();
            if (lines.Count > 0 && lines[0] != Header)
            {
                lines[0] = Header; // WT-P6, ACCT-4: older files get the model and type columns, empty in the old rows
                File.WriteAllLines(FilePath, lines);
            }

            var from = clock.Now - Window;
            foreach (var row in Rows(lines))
            {
                if (!TryFinished(row, out var finished) || finished < from)
                {
                    continue;
                }

                stats.Add(new RunStat
                {
                    Finished = finished,
                    Result = Field(row, "result"),
                    Minutes = Whole(Field(row, "minutes")),
                    Model = Field(row, "model"),
                    Card = IdOf(Field(row, "card")),
                });
            }
        }
        catch (Exception e)
        {
            // A file that cannot be read leaves the figures empty, as Seed-RunStats' catch did.
            Told("the run figures", e);
        }

        _runs.Clear();
        _runs.AddRange(stats);
        return stats;
    }

    /// <summary>
    /// The runs of the last <see cref="Trends.WindowDays"/> local days out of the file's own rows,
    /// which is how the Trends window (630-A) reaches further back than the 48 hours of
    /// <see cref="Runs"/> (ACCT-4). The rows are kept from one page rebuild to the next and re-read
    /// only when the file's last write moves; a file that is missing, empty or held open leaves the
    /// chart on the figures Host already holds, which is 630-A's fallback, kept here where the file
    /// is read (770-B). Core does the parsing (<see cref="Trends.RunsFromRows"/>); this only reads.
    /// </summary>
    public IReadOnlyList<RunStat> TrendRuns(DateTimeOffset now)
    {
        string[] lines;
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(FilePath);
            lock (_trendLock)
            {
                if (_trend is { } cached && cached.Written == written)
                {
                    return cached.Runs;
                }
            }

            if (!File.Exists(FilePath))
            {
                return _runs; // 1118-B: no runs yet, nothing to say
            }

            lines = File.ReadAllLines(FilePath);
        }
        catch (Exception e)
        {
            // As SeedRunStats' catch did: the figures stand in, and the log says the file was read for nothing.
            Told("the trends", e);
            return _runs;
        }

        if (lines.Length == 0)
        {
            return _runs;
        }

        var trend = Trends.RunsFromRows(lines, now);
        lock (_trendLock)
        {
            _trend = (written, trend);
        }

        return trend;
    }

    /// <summary>
    /// <c>Seed-Recent</c> (L812): the last rows of the file, newest first, up to ten, each with
    /// the newest <c>&lt;id&gt;-&lt;start&gt;.log</c> that began no later than a minute after the
    /// run finished. Rows already known are not added again.
    /// </summary>
    public IReadOnlyList<RecentEvent> SeedRecent()
    {
        try
        {
            if (_recent.Count >= 10 || !File.Exists(FilePath))
            {
                return _recent;
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in _recent)
            {
                known.Add(Key(e.Card, e.When));
            }

            var logs = Logs();
            foreach (var row in Rows(File.ReadAllLines(FilePath)).Reverse())
            {
                if (_recent.Count >= 10)
                {
                    break;
                }

                if (!TryFinished(row, out var when))
                {
                    continue;
                }

                var card = Field(row, "card");
                if (!known.Add(Key(card, when)))
                {
                    continue;
                }

                var id = IdOf(card);
                var until = when.LocalDateTime.AddMinutes(1);
                string? logFile = null;
                var best = DateTime.MinValue;
                foreach (var found in logs)
                {
                    if (string.Equals(found.Id, id, StringComparison.Ordinal)
                        && found.Start <= until && found.Start > best)
                    {
                        best = found.Start;
                        logFile = found.Path;
                    }
                }

                _recent.Add(new RecentEvent
                {
                    When = when,
                    Slot = Whole(Field(row, "slot")),
                    Type = Field(row, "type"),
                    Model = Field(row, "model"),
                    Card = card,
                    Result = Field(row, "result"),
                    Minutes = Whole(Field(row, "minutes")),
                    Log = logFile,
                });
            }
        }
        catch (Exception e)
        {
            Told("recent events", e);
        }

        return _recent;
    }

    // 1118-B: a read error is logged when it first happens and again only when its text changes.
    private void Told(string what, Exception e)
    {
        lock (_told)
        {
            if (_told.TryGetValue(what, out var last) && last == e.Message)
            {
                return;
            }

            _told[what] = e.Message;
        }

        log?.Write($"could not read {FilePath} for {what}: {e.Message}");
    }

    private static string Key(string card, DateTimeOffset when) =>
        card + "|" + when.ToString("s", CultureInfo.InvariantCulture);

    private static bool TryFinished(IReadOnlyDictionary<string, string> row, out DateTimeOffset when) =>
        DateTimeOffset.TryParse(
            Field(row, "finished"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out when);

    private static string Field(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var value) ? value : "";

    private static int Whole(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string IdOf(string card)
    {
        var match = CardId.Match(card);
        return match.Success ? match.Groups[1].Value : card;
    }

    /// <summary>The log files that name a card and a start time (WRK-9).</summary>
    private IEnumerable<(string Id, DateTime Start, string Path)> Logs()
    {
        var found = new List<(string, DateTime, string)>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(logDir, "*.log"))
            {
                var match = LogName.Match(System.IO.Path.GetFileName(path));
                if (match.Success
                    && DateTime.TryParseExact(
                        match.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                {
                    found.Add((match.Groups[1].Value, start, path));
                }
            }
        }
        catch (Exception e)
        {
            // No logs is only ever a missing link; the events are still worth showing.
            log?.Write($"could not list the logs in {logDir}: {e.Message}");
        }

        return found;
    }

    /// <summary>The file's rows as name/value, read against the file's own header, as Import-Csv does.</summary>
    private static IEnumerable<Dictionary<string, string>> Rows(IEnumerable<string> lines)
    {
        var all = lines as IReadOnlyList<string> ?? lines.ToList();
        if (all.Count == 0)
        {
            return [];
        }

        var header = Split(all[0]);
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < all.Count; i++)
        {
            if (all[i].Length == 0)
            {
                continue;
            }

            var fields = Split(all[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var f = 0; f < header.Length && f < fields.Length; f++)
            {
                row[header[f]] = fields[f];
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Splits one CSV line, honouring the quoted stop reason (ACCT-1).</summary>
    internal static string[] Split(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    _ = current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                _ = current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return [.. parts];
    }
}
