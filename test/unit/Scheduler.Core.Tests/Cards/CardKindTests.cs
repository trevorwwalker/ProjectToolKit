// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>1356-A: EST-1 to EST-3, the card's <c>Kind:</c> against the project's list.</summary>
public class CardKindTests
{
    private const string Jobs = "C:/repo/jobs";

    private static Card Parse(string text, IReadOnlyList<string>? kinds = null) =>
        CardFiles.Parse(Jobs, $"{Jobs}/queue/1356-A-x.md", text, kinds) ?? throw new InvalidOperationException("not a card");

    private static Card WithHeader(string line, IReadOnlyList<string>? kinds = null) =>
        Parse($"# 1356-A: t\n\nCategory: A\n{line}\nStatus: ready\n\n## Task\n", kinds);

    [Theory]
    [InlineData("Kind: Docs", "docs")]
    [InlineData("Kind:   check  ", "check")]
    [InlineData("Kind: design (set by the planner)", "design")]
    public void EST_1_KindReadsFirstWordLowercase(string line, string expected) =>
        Assert.Equal(expected, WithHeader(line).Kind);

    [Fact]
    public void EST_1_FirstKindLineWins() =>
        Assert.Equal("docs", Parse("# t\n\nKind: docs\nKind: check\nStatus: ready\n\n## Task\n").Kind);

    [Fact]
    public void EST_1_KindBelowTheHeaderIsIgnored() =>
        Assert.Equal("", Parse("# t\n\nStatus: ready\n\n## Task\nKind: docs\n").Kind);

    [Fact]
    public void EST_2_NoList_UsesTheFourDefaults()
    {
        Assert.Equal(["design", "implementation", "docs", "check"], SchedulerConfig.DefaultCardKinds);
        Assert.Equal(SchedulerConfig.DefaultCardKinds, Load("{}").CardKinds);
        Assert.Equal(SchedulerConfig.DefaultCardKinds, Load("""{"cardKinds":[]}""").CardKinds);
        Assert.Equal(SchedulerConfig.DefaultCardKinds, Load("""{"cardKinds":"design"}""").CardKinds);
        Assert.Equal("implementation", WithHeader("Kind: implementation").Kind);
    }

    [Fact]
    public void EST_2_ProjectList_ReplacesTheDefaults()
    {
        var kinds = Load("""{"cardKinds":["design","build","test","docs"]}""").CardKinds;

        Assert.Equal(["design", "build", "test", "docs"], kinds);
        Assert.Equal("build", WithHeader("Kind: Build", kinds).Kind);
        Assert.Equal("", WithHeader("Kind: implementation", kinds).Kind);
    }

    [Theory]
    [InlineData("Kind: impl")]
    [InlineData("Kind:")]
    [InlineData("Priority: normal")]
    public void EST_3_UnknownEmptyOrMissing_IsEmpty(string line) =>
        Assert.Equal("", WithHeader(line).Kind);

    [Fact]
    public void EST_3_UnknownWord_IsLoggedOncePerCardAndBlob()
    {
        var log = new UnknownKindLog();
        var card = WithHeader("Kind: impl");

        Assert.Equal("card 1356-A-x: Kind: impl is not one of design, implementation, docs, check", log.Once(card, "b1"));
        Assert.Null(log.Once(card, "b1"));
        Assert.Null(log.Once(card, "b1"));
        Assert.NotNull(log.Once(card, "b2"));
        Assert.Null(log.Once(WithHeader("Kind: docs"), "b3"));
        Assert.Null(log.Once(WithHeader("Priority: normal"), "b4"));
    }

    private static SchedulerConfig Load(string json) =>
        SchedulerConfigLoader.Load(json, "C:/repo", "C:/local", null);
}
