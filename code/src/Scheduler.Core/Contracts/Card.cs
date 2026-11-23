namespace Scheduler.Core.Contracts;

/// <summary>
/// A job card read from main (A2). Cards in done/ are not opened (CARD-5): only Folder, Path,
/// Name, Num, Category, Id and BlobId are set for them - the last from the tree, not the text.
/// </summary>
public sealed record Card
{
    /// <summary>The folder it is in (CARD-4).</summary>
    public required CardFolder Folder { get; init; }

    /// <summary>The path in the repository, as git ls-tree gives it.</summary>
    public required string Path { get; init; }

    /// <summary>The file name without <c>.md</c> (CARD-4).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The leading number as a value (CARD-4), which is what order is taken from (DISP-14,
    /// PAGE-6): 999-A before 1000-A. A card number is a positive integer of any width (931-A).
    /// </summary>
    public required long Num { get; init; }

    /// <summary>The category letter, as written in the file name (CARD-4, G-1).</summary>
    public required string Category { get; init; }

    /// <summary>
    /// <c>{number}-{Category}</c> with the digits exactly as the file name writes them (CARD-4,
    /// 931-A): <c>007-A</c> and <c>1001-A</c> keep their width; there is no padding.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The first <c># </c> line (CARD-6).</summary>
    public string Title { get; init; } = "";

    /// <summary>The first <c>Status:</c> line with its continuation lines (CARD-7).</summary>
    public string Status { get; init; } = "";

    /// <summary>0 high, 1 normal, 2 low (CARD-8).</summary>
    public int Prio { get; init; } = 1;

    /// <summary>The <c>Time box:</c> in minutes, or null (CARD-9).</summary>
    public int? TimeBox { get; init; }

    /// <summary>The <c>Worker:</c> request, or null when the card has no Worker line (CARD-10).</summary>
    public string? WorkerReq { get; init; }

    /// <summary>
    /// The text after the first <c>Scope:</c> line, or null when the card has none (spec B2 SC-1,
    /// job card 560-A).
    /// </summary>
    public string? ScopeLine { get; init; }

    /// <summary>
    /// The text after the first <c>Locks:</c> line, or null when the card has none; its paths are
    /// added to the card's scope (SC-1, SC-4, job card 560-A).
    /// </summary>
    public string? LocksLine { get; init; }

    /// <summary>The header's <c>Category:</c> (or old <c>Category:</c>) value, or null (969-A). The id's letter still comes from the file name.</summary>
    public string? HeaderCategory { get; init; }

    /// <summary>1030-A: the header's <c>Type:</c>, lower case: <c>feature</c>, <c>bug</c>, or "" (untyped).</summary>
    public string CardType { get; init; } = "";

    /// <summary>1030-A: the header's <c>Version:</c> (bug cards), "" when none.</summary>
    public string Version { get; init; } = "";

    /// <summary>1030-A: the plan file named by the header's <c>Plan:</c>, "" when none or "none...".</summary>
    public string PlanPath { get; init; } = "";

    /// <summary>1030-A: what follows the plan file on the <c>Plan:</c> line (step or phase), free text.</summary>
    public string PlanStep { get; init; } = "";

    /// <summary>The ids on the last <c>Depends on:</c> line (CARD-11).</summary>
    public IReadOnlyList<string> Depends { get; init; } = [];

    /// <summary>The unanswered questions (CARD-12).</summary>
    public IReadOnlyList<CardQuestion> Questions { get; init; } = [];

    /// <summary>Why it waits on the developer (CARD-13).</summary>
    public string Why { get; init; } = "";

    /// <summary><c>status</c> or <c>result</c>: where <see cref="Why"/> came from (CARD-13).</summary>
    public string WhyFrom { get; init; } = "";

    /// <summary>The newest <c>## Result</c> journal entry, shortened (CARD-14).</summary>
    public string LatestJournal { get; init; } = "";

    /// <summary>
    /// The blob id main had for the card when it was read (CARD-1), empty when the reader had none.
    /// Host sets it from <c>git ls-tree</c> so a card whose text changed since it was flagged by
    /// 395-B's <c>FinishedCardCheck</c> can be told apart from one that never changed (job card 410-A).
    /// </summary>
    public string BlobId { get; init; } = "";
}
