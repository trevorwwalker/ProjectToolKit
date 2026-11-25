// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>One entry of the Waiting on you window (PAGE-5).</summary>
public sealed record PageWaitingItem
{
    /// <summary>What kind of entry it is.</summary>
    public required PageWaitingKind Kind { get; init; }

    /// <summary>The card file name, or the category letter for a held category.</summary>
    public required string Name { get; init; }

    /// <summary>A line of explanation.</summary>
    public string Note { get; init; } = "";

    /// <summary>The open questions, for a question entry.</summary>
    public IReadOnlyList<CardQuestion> Questions { get; init; } = [];

    /// <summary>The card title, or empty for a held category (510-B).</summary>
    public string Title { get; init; } = "";

    /// <summary>Where the note came from (CARD-13): <c>status</c>, <c>result</c> when taken from the journal, or empty (510-B).</summary>
    public string NoteFrom { get; init; } = "";

    /// <summary>The result word of the last run, for a stuck row; empty when unknown (940-A).</summary>
    public string LastResult { get; init; } = "";

    /// <summary>The log file of the last run, for a stuck row's log link; empty when unknown (940-A).</summary>
    public string Log { get; init; } = "";
}
