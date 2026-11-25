// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;

namespace Scheduler.Core.Tests.Moves;

/// <summary>
/// Dynamic locks (design <c>docs/design/tdd/scheduler/dynamic-locks.md</c>, card 1017-A): section 7's
/// Audit, "a stray turns To: done into To: review" (DL-19, developer's answer to 994-D Q5). Pure.
/// </summary>
public class MovesStrayTests
{
    private const string Text = "# 990-B: x\n\nCategory: B\nStatus: done\n\n## Result\n- did it\n";

    private static CardRequest Request(CardFolder to, string? text = Text) =>
        new() { FileName = "20261121-100000-000-slot1.move", Card = "990-B-x", To = to, Slot = 1, Text = text };

    [Fact]
    public void DL19_StraysDone_BecomesReview_WithTheStatusNamingThePath()
    {
        var r = CardMoves.Stray(Request(CardFolder.Done), "docs/design/x.md");
        Assert.Equal(CardFolder.Review, r.To);
        Assert.Contains("Status: review: changed docs/design/x.md without its lock\n", r.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: done", r.Text, StringComparison.Ordinal);
        Assert.Contains("- did it", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DL19_StraysDoneWithNoText_SetsTheRequestsStatus()
    {
        var r = CardMoves.Stray(Request(CardFolder.Done, text: null), "docs/design/x.md");
        Assert.Equal(CardFolder.Review, r.To);
        Assert.Equal("review: changed docs/design/x.md without its lock", r.Status);
    }

    [Theory]
    [InlineData(CardFolder.Review)]
    [InlineData(CardFolder.Active)]
    [InlineData(CardFolder.Queue)]
    public void DL19_OtherMoves_AreUnchanged(CardFolder to)
    {
        var req = Request(to);
        Assert.Same(req, CardMoves.Stray(req, "docs/design/x.md"));
    }

    [Fact]
    public void DL19_NoStrayPath_IsUnchanged()
    {
        var req = Request(CardFolder.Done);
        Assert.Same(req, CardMoves.Stray(req, ""));
    }
}
