// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>Everything the review panel <c>/review?card=</c> shows about one card (roadmap item 6, card 650-C).</summary>
public sealed record ReviewPanel
{
    /// <summary>The card file name.</summary>
    public required string Name { get; init; }

    /// <summary>The card title.</summary>
    public string Title { get; init; } = "";

    /// <summary>The Status line, <c>review: ...</c>.</summary>
    public string Status { get; init; } = "";

    /// <summary>The card's questions, answered or not.</summary>
    public IReadOnlyList<CardQuestion> Questions { get; init; } = [];

    /// <summary>The commits that name the card since its claim, oldest first.</summary>
    public IReadOnlyList<ReviewCommit> Commits { get; init; } = [];

    /// <summary>The text of the card's Result section, Markdown.</summary>
    public string Result { get; init; } = "";

    /// <summary>Repository files the Result names that exist, as repository-relative paths.</summary>
    public IReadOnlyList<string> Links { get; init; } = [];
}
