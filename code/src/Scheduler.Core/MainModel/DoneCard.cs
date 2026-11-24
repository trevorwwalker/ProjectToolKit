using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.MainModel;

/// <summary>
/// A done/ or archive/ card's header, as the views need it (state-cache.md section 1, VW-21):
/// read once per blob in the model's second load phase (MM-5); the tick never reads it. Strings
/// that repeat across thousands of cards (category, folder, plan, worker) are interned.
/// </summary>
public sealed record DoneCard
{
    /// <summary>The longest Status kept (VW-13's cut).</summary>
    public const int StatusLength = 200;

    /// <summary><c>done</c> or <c>archive</c>.</summary>
    public required string Folder { get; init; }

    /// <summary>The path in the repository.</summary>
    public required string Path { get; init; }

    /// <summary>The file name without <c>.md</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The card number's value.</summary>
    public required long Num { get; init; }

    /// <summary>The category letter from the file name.</summary>
    public required string Category { get; init; }

    /// <summary><c>&lt;num&gt;-&lt;L&gt;</c>, the digits as the file name writes them.</summary>
    public required string Id { get; init; }

    /// <summary>The blob the header was read from.</summary>
    public string BlobId { get; init; } = "";

    /// <summary>The <c># </c> line.</summary>
    public string Title { get; init; } = "";

    /// <summary>The Status line, cut at <see cref="StatusLength"/> characters.</summary>
    public string Status { get; init; } = "";

    /// <summary>The Type line.</summary>
    public string CardType { get; init; } = "";

    /// <summary>The Plan line's path.</summary>
    public string PlanPath { get; init; } = "";

    /// <summary>The Plan line's step.</summary>
    public string PlanStep { get; init; } = "";

    /// <summary>The Version line.</summary>
    public string Version { get; init; } = "";

    /// <summary>The card ids of the Depends on line.</summary>
    public IReadOnlyList<string> Depends { get; init; } = [];

    /// <summary>The Time box in minutes, if any.</summary>
    public int? TimeBox { get; init; }

    /// <summary>The Worker line, if any.</summary>
    public string? WorkerReq { get; init; }

    /// <summary>The Scope line, if any (1141-A).</summary>
    public string? ScopeLine { get; init; }
}

/// <summary>The done/archive header parse (views.md's header-only parse, MM-5).</summary>
public static class DoneHeaders
{
    /// <summary>
    /// <c>{jobsDir}/(done|archive)/{num}-{L}-{slug}.md</c> as a header with no text fields yet;
    /// any other path gives null (as CARD-4 does for the open folders).
    /// </summary>
    public static DoneCard? FromPath(string jobsDir, string path)
    {
        var m = Text.Match(path, @"^" + Regex.Escape(jobsDir) + @"/(done|archive)/((\d+)-([A-Z])-[^/]+)\.md$");
        if (!m.Success
            || !long.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
        {
            return null;
        }

        var category = string.Intern(m.Groups[4].Value);
        return new DoneCard
        {
            Folder = string.Intern(m.Groups[1].Value.ToLowerInvariant()),
            Path = path,
            Name = m.Groups[2].Value,
            Num = num,
            Category = category,
            Id = m.Groups[3].Value + "-" + category,
        };
    }

    /// <summary>
    /// The header fields of <paramref name="text"/> onto <paramref name="header"/>: only the text
    /// before the first <c>## </c> section is parsed, by the same field rules as an open card
    /// (<see cref="CardFiles.ParseText"/>, 760-A), so a done card and an open card agree.
    /// </summary>
    public static DoneCard Parse(DoneCard header, string text, string blobId)
    {
        var cut = text.StartsWith("## ", StringComparison.Ordinal) ? 0 : text.IndexOf("\n## ", StringComparison.Ordinal);
        var head = cut < 0 ? text : text[..cut];
        var probe = new Card
        {
            Folder = CardFolder.Done,
            Path = header.Path,
            Name = header.Name,
            Num = header.Num,
            Category = header.Category,
            Id = header.Id,
        };
        return From(header, CardFiles.ParseText(probe, head), blobId);
    }

    /// <summary>
    /// The header of a card the model already holds parsed (a card moved into done/ or archive/
    /// with its blob unchanged): no text is read (MM-7).
    /// </summary>
    public static DoneCard From(DoneCard header, Card card, string blobId) => header with
    {
        BlobId = blobId,
        Title = card.Title,
        Status = card.Status.Length > DoneCard.StatusLength ? card.Status[..DoneCard.StatusLength] : card.Status,
        CardType = card.CardType,
        PlanPath = string.Intern(card.PlanPath),
        PlanStep = card.PlanStep,
        Version = card.Version,
        Depends = card.Depends,
        TimeBox = card.TimeBox,
        ScopeLine = card.ScopeLine,
        WorkerReq = card.WorkerReq is null ? null : string.Intern(card.WorkerReq),
    };
}
