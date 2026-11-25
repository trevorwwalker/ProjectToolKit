// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;

namespace Scheduler.Core.Accounting;

/// <summary>
/// One finished run as accounting.csv keeps it (ACCT-1, <c>Account</c> L844). The file itself is
/// Host's (140-B); this is the header and the row that goes in it.
/// </summary>
public sealed record AccountingRow
{
    /// <summary>The line a new accounting.csv starts with (ACCT-1).</summary>
    public const string Header = "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model";

    /// <summary>When the run finished; written as <c>Get-Date -Format s</c>.</summary>
    public required DateTimeOffset Finished { get; init; }

    /// <summary>The card name, file name without <c>.md</c>.</summary>
    public required string Card { get; init; }

    /// <summary>The card's category.</summary>
    public required string Category { get; init; }

    /// <summary>The slot it ran in.</summary>
    public required int Slot { get; init; }

    /// <summary>The result the worker ended with.</summary>
    public required string Result { get; init; }

    /// <summary>Whole minutes it ran, from <see cref="MinutesOf"/>.</summary>
    public required int Minutes { get; init; }

    /// <summary>Bytes of the readable log, 0 when there is none.</summary>
    public required long LogBytes { get; init; }

    /// <summary>Why it was stopped or ended; the only field that carries quotes.</summary>
    public required string StopReason { get; init; }

    /// <summary>The model that really ran, from <see cref="ModelColumn.Resolve"/>.</summary>
    public required string Model { get; init; }

    /// <summary>1030-A: the card's <c>Type:</c> (<c>feature</c>, <c>bug</c> or "" untyped), the <c>card_type</c> column.</summary>
    public string CardType { get; init; } = "";

    /// <summary>1030-A: the card's <c>Version:</c> header, the <c>version</c> column; "" when none.</summary>
    public string Version { get; init; } = "";

    /// <summary>EST-5: the card's kind (<c>Card.Kind</c>) when the run started, the <c>kind</c> column; "" when none.</summary>
    public string Kind { get; init; } = "";

    /// <summary>
    /// 1030-A: the columns Host appends after its own <c>type</c> (the worker's type; the card's type is
    /// <c>card_type</c> so the two do not clash). Last, so old rows and readers still work.
    /// </summary>
    public const string CardColumns = ",card_type,version,kind";

    /// <summary>1030-A: this row's values for <see cref="CardColumns"/>, with the leading comma.</summary>
    public string CardFields() => "," + Clean(CardType) + "," + Clean(Version) + "," + Clean(Kind);

    private static string Clean(string value) => value.Replace(',', ' ').ReplaceLineEndings(" ").Trim();

    /// <summary>
    /// The row (ACCT-1). Only <see cref="StopReason"/> is quoted, and its own quotes are not
    /// escaped, so a <c>"</c> in it or a comma in a card name breaks the row (flag C13).
    /// </summary>
    public string ToCsvLine() => string.Format(
        CultureInfo.InvariantCulture,
        "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
        Finished.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        Card,
        Category,
        Slot,
        Result,
        Minutes,
        LogBytes,
        "\"" + StopReason + "\"",
        Model);

    /// <summary>
    /// Whole minutes of a run's age (L2812). PowerShell's <c>[int]</c> casts round to the nearest
    /// and halves to even, which is <see cref="MidpointRounding.ToEven"/>, not a C# cast.
    /// </summary>
    public static int MinutesOf(TimeSpan age) => (int)Math.Round(age.TotalMinutes, MidpointRounding.ToEven);
}
