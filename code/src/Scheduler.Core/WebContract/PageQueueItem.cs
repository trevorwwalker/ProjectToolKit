// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>One row of the Queue window (PAGE-5, PAGE-6): a card in dispatch order with its note.</summary>
public sealed record PageQueueItem
{
    /// <summary>The card file name.</summary>
    public required string Name { get; init; }

    /// <summary>The card title.</summary>
    public string Title { get; init; } = "";

    /// <summary>The note beside it (why it waits, which category holds it), or empty.</summary>
    public string Note { get; init; } = "";

    /// <summary>The cards it depends on, as its Depends on line names them (510-B).</summary>
    public IReadOnlyList<string> Depends { get; init; } = [];

    /// <summary>The log of its newest run, or empty (510-B).</summary>
    public string Log { get; init; } = "";

    /// <summary>
    /// The time-box warning or suggestion beside the card, or empty (roadmap item 4, card 640-A):
    /// what its area's finished runs say the worker it is headed for usually needs.
    /// </summary>
    public string TimeBoxNote { get; init; } = "";

    /// <summary>Dynamic locks (DL-25): "yielded: waits for `path` held by `card`", or "".</summary>
    public string Yielded { get; init; } = "";

    /// <summary>1030-A: the card's type (<c>feature</c>, <c>bug</c> or "" untyped), for a later page card to show and filter.</summary>
    public string CardType { get; init; } = "";

    /// <summary>1030-A: the plan file the card names, "" when none.</summary>
    public string PlanPath { get; init; } = "";

    /// <summary>1030-A: the step or phase after the plan file, free text.</summary>
    public string PlanStep { get; init; } = "";
}
