// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;

namespace Scheduler.Core.Accounting;

/// <summary>
/// One line of accounting.csv read by the header's column names (EST-8). A column the line does not
/// have (an older row, from before the column was added) reads as "".
/// </summary>
public sealed class ParsedAccountingRow
{
    private readonly Dictionary<string, string> _fields;

    internal ParsedAccountingRow(Dictionary<string, string> fields, DateTimeOffset finished, int minutes)
    {
        _fields = fields;
        Finished = finished;
        Minutes = minutes;
    }

    /// <summary>When the run finished; the file's local time read with the machine's own offset (ACCT-4).</summary>
    public DateTimeOffset Finished { get; }

    /// <summary>The run's whole minutes.</summary>
    public int Minutes { get; }

    /// <summary>The field of this column, "" when the row has no such column or an older row lacks it.</summary>
    public string this[string column] => _fields.TryGetValue(column, out var value) ? value : "";
}

/// <summary>The rows <see cref="AccountingRows.Parse"/> read, and how many lines it skipped.</summary>
public sealed record AccountingRowsResult(IReadOnlyList<ParsedAccountingRow> Rows, int Skipped);

/// <summary>
/// The one reader of accounting.csv's lines, by the header's column names (EST-8). Three readers
/// once counted fields from the end or the front, and one broke when columns were added; reading by
/// name is how the next column cannot break them. The file's header may or may not have the
/// <c>kind</c> column (EST-5, EST-7), or any other that a newer or older file lacks.
/// </summary>
public static class AccountingRows
{
    /// <summary>
    /// Reads the lines: the first line that starts with <c>finished</c> is the header, and a file
    /// with none is read with <see cref="AccountingRow.Header"/>'s columns. A line that does not start
    /// with a digit, or whose <c>finished</c> or <c>minutes</c> does not parse, is skipped and counted.
    /// A field that starts with a quote runs to the next field that ends with one, so a comma inside
    /// the stop reason keeps the columns aligned. Blank lines are neither rows nor skipped.
    /// </summary>
    public static AccountingRowsResult Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var columns = AccountingRow.Header.Split(',');
        var rows = new List<ParsedAccountingRow>();
        var skipped = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = Split(line);
            if (fields[0] == "finished")
            {
                columns = [.. fields];
                continue;
            }

            if (fields[0].Length == 0 || fields[0][0] < '0' || fields[0][0] > '9'
                || !DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var finished)
                || fields.Count <= 5
                || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            {
                skipped++;
                continue;
            }

            var named = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Length && i < fields.Count; i++)
            {
                named[columns[i]] = fields[i];
            }

            rows.Add(new ParsedAccountingRow(named, finished, minutes));
        }

        return new AccountingRowsResult(rows, skipped);
    }

    // Splits on commas; a field that starts with a quote and does not end with one runs on to the next
    // field that ends with one (the commas between are kept). The quotes themselves are dropped.
    private static List<string> Split(string line)
    {
        var parts = line.Split(',');
        var fields = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length > 0 && part[0] == '"' && (part.Length == 1 || part[^1] != '"'))
            {
                var joined = part;
                var j = i + 1;
                while (j < parts.Length)
                {
                    joined += "," + parts[j];
                    if (parts[j].EndsWith('"'))
                    {
                        break;
                    }

                    j++;
                }

                i = Math.Min(j, parts.Length - 1);
                part = joined;
            }

            fields.Add(part.Length >= 2 && part[0] == '"' && part[^1] == '"' ? part[1..^1] : part);
        }

        return fields;
    }
}
