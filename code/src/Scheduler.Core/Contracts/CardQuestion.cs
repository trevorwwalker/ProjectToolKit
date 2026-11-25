// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>An unanswered <c>- Q&lt;n&gt;: &lt;text&gt; [&lt;c1&gt; | &lt;c2&gt; ...]</c> line under <c>## Questions</c> (CARD-12).</summary>
public sealed record CardQuestion
{
    /// <summary>The n of Q&lt;n&gt;.</summary>
    public required int Number { get; init; }

    /// <summary>The question's text, before the choices.</summary>
    public required string Text { get; init; }

    /// <summary>The choices, at least two.</summary>
    public IReadOnlyList<CardChoice> Choices { get; init; } = [];
}
