// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>1472-A: the header's <c>Fixes-red:</c> line.</summary>
public class CardFixesRedTests
{
    private const string Jobs = "C:/repo/jobs";

    private static Card Parse(string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/queue/1472-A-x.md", text) ?? throw new InvalidOperationException("not a card");

    [Theory]
    [InlineData("Fixes-red: yes", true)]
    [InlineData("Fixes-red: YES", true)]
    [InlineData("Fixes-red:   Yes  ", true)]
    [InlineData("Fixes-red: no", false)]
    [InlineData("Fixes-red: yes please", true)]
    [InlineData("Fixes-red: maybe", false)]
    [InlineData("Fixes-red:", false)]
    public void FixesRed_ReadsYesInAnyCase(string line, bool expected) =>
        Assert.Equal(expected, Parse($"# 1472-A: t\n\nCategory: A\n{line}\nStatus: ready\n\n## Task\n").FixesRed);

    [Fact]
    public void FixesRed_NoLine_IsFalse() =>
        Assert.False(Parse("# 1472-A: t\n\nCategory: A\nStatus: ready\n\n## Task\n").FixesRed);

    [Fact]
    public void FixesRed_FirstLineWins() =>
        Assert.False(Parse("# t\n\nFixes-red: no\nFixes-red: yes\nStatus: ready\n\n## Task\n").FixesRed);

    [Fact]
    public void FixesRed_BelowTheTaskHeading_IsFalse() =>
        Assert.False(Parse("# t\n\nStatus: ready\n\n## Task\nFixes-red: yes\n").FixesRed);
}
