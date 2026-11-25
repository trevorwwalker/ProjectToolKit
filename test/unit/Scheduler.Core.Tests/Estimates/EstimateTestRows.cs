// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Estimates;

/// <summary>Fixed rows for the estimates tests: accounting.csv lines read by <see cref="AccountingRows"/>.</summary>
internal static class EstimateTestRows
{
    public const string Header = "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version,kind";

    public static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One line: finished is <paramref name="daysAgo"/> days (and <paramref name="minutesAgo"/> minutes) before <see cref="Now"/>, as local text.</summary>
    public static string Line(
        string result = "done",
        int minutes = 10,
        string type = "Opus",
        string kind = "",
        string lane = "A",
        string card = "1-A-x",
        string stop = "",
        string model = "claude-opus-5-5",
        double daysAgo = 1,
        int minutesAgo = 0)
    {
        var when = Now.LocalDateTime.AddDays(-daysAgo).AddMinutes(-minutesAgo);
        return string.Join(
            ',',
            when.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            card,
            lane,
            "1",
            result,
            minutes.ToString(CultureInfo.InvariantCulture),
            "10",
            "\"" + stop + "\"",
            model,
            type,
            "feature",
            "",
            kind);
    }

    public static IReadOnlyList<ParsedAccountingRow> Rows(params string[] lines) => AccountingRows.Parse([Header, .. lines]).Rows;

    /// <summary>The same line <paramref name="count"/> times, each a minute older than the last (so every one is a different run).</summary>
    public static IEnumerable<string> Many(int count, Func<int, string> line) => Enumerable.Range(0, count).Select(line);
}
