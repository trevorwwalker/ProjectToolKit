// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>One choice of a card question; a choice ending <c>-&gt; done|queue|active</c> moves the card there (CARD-12).</summary>
public sealed record CardChoice
{
    /// <summary>The choice's text as shown, without the destination.</summary>
    public required string Label { get; init; }

    /// <summary>Where choosing it moves the card, or null when it does not move it.</summary>
    public CardFolder? Destination { get; init; }
}
