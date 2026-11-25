// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>1030-A: Type, Version and Plan are read from the card's header only.</summary>
public class CardTypeFieldsTests
{
    private const string Jobs = "C:/repo/jobs";

    private static Card Parse(string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/queue/1030-A-x.md", text) ?? throw new InvalidOperationException("not a card");

    [Theory]
    [InlineData("Type: feature", "feature")]
    [InlineData("Type: bug", "bug")]
    [InlineData("Type: BUG", "bug")]
    [InlineData("type: Feature", "feature")]
    [InlineData("Type: chore", "")]
    [InlineData("Type:", "")]
    [InlineData("Priority: normal", "")]
    public void CardType_FromHeader(string line, string expected) =>
        Assert.Equal(expected, Parse($"# 1030-A: t\n\nCategory: A\n{line}\nStatus: ready\n\n## Task\n").CardType);

    [Fact]
    public void CardType_InBody_IsIgnored() =>
        Assert.Equal("", Parse("# t\n\nStatus: ready\n\n## Task\nType: bug\nVersion: 9.9.9\nPlan: x.md step 1\n").CardType);

    [Fact]
    public void Version_FromHeaderOnly()
    {
        var card = Parse("# t\n\nType: bug\nVersion: 3.0.61\nStatus: ready\n\n## Task\nVersion: 9.9.9\n");
        Assert.Equal("3.0.61", card.Version);
        Assert.Equal("", Parse("# t\n\nStatus: ready\n\n## Task\nVersion: 1.0\n").Version);
    }

    [Theory]
    [InlineData("Plan: jobs/plans/machines_plan.md, step 3", "jobs/plans/machines_plan.md", "step 3")]
    [InlineData("Plan: jobs/plans/x.md phase 1", "jobs/plans/x.md", "phase 1")]
    [InlineData("Plan: docs/design/tdd/spec.md section 7", "docs/design/tdd/spec.md", "section 7")]
    [InlineData("Plan: jobs/plans/x.md", "jobs/plans/x.md", "")]
    [InlineData("Plan: none (developer, 2026-11-21 16:43)", "", "")]
    [InlineData("Plan: none", "", "")]
    [InlineData("Plan:", "", "")]
    public void Plan_PathAndStep(string line, string path, string step)
    {
        var card = Parse($"# t\n\n{line}\nStatus: ready\n\n## Task\n");
        Assert.Equal(path, card.PlanPath);
        Assert.Equal(step, card.PlanStep);
    }

    [Fact]
    public void Plan_InBody_IsIgnored()
    {
        var card = Parse("# t\n\nStatus: ready\n\n## Task\nPlan: jobs/plans/x.md step 1\n");
        Assert.Equal("", card.PlanPath);
        Assert.Equal("", card.PlanStep);
    }

    [Fact]
    public void OldCard_IsUntyped()
    {
        var card = Parse("# 1-A: old\n\nLane: A\nStatus: ready\n\n## Task\n");
        Assert.Equal("", card.CardType);
        Assert.Equal("", card.Version);
        Assert.Equal("", card.PlanPath);
    }
}
