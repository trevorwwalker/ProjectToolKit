// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>The shared step and card labels of the Plans and Gantt views (cards 1289-C, 1299-C, 1318-A).</summary>
public class PlanLabelsTests
{
    [Theory]
    [InlineData("2", "2. Build the job system: cards", "Build the job system: cards")]
    [InlineData("2", "2) Build", "Build")]
    [InlineData("2", "2 Build", "Build")]
    [InlineData("1.2", "1.2 Sub thing", "Sub thing")]
    [InlineData("4", "4. 404 pages", "404 pages")]
    [InlineData("2", "2x Speed", "2x Speed")]
    [InlineData("2", "Two", "Two")]
    [InlineData("2", "2", "2")]
    public void StepTitle_DropsTheNumberItStartsWith(string label, string title, string expected) =>
        Assert.Equal(expected, PlanLabels.StepTitle(label, title));

    [Theory]
    [InlineData("1274-B", "1274-B: After a restart", "After a restart")]
    [InlineData("1274-B", "1274-B After a restart", "After a restart")]
    [InlineData("1274-b", "1274-B: After", "After")]
    [InlineData("1274-B", "1274-B", "")]
    [InlineData("1274-B", "After a restart", "After a restart")]
    [InlineData("1274-B", "1274-Bx thing", "1274-Bx thing")]
    public void CardTitle_DropsTheIdItStartsWith(string id, string title, string expected) =>
        Assert.Equal(expected, PlanLabels.CardTitle(id, title));

    [Theory]
    [InlineData("", "2", "2")]
    [InlineData("2", "7", "2.7")]
    [InlineData("2.10", "8", "2.10.8")]
    [InlineData("2", "7.1", "2.7.1")]
    public void Shown_IsThePathInThePlanTree(string prefix, string label, string expected) =>
        Assert.Equal(expected, PlanLabels.Shown(prefix, label));

    [Fact]
    public void StepText_And_CardText_ShowEachNumberOnce()
    {
        Assert.Equal("2.7. Seven", PlanLabels.StepText("2", "7", "7. Seven"));
        Assert.Equal("1312-D: Design", PlanLabels.CardText("1312-D", "1312-D: Design"));
        Assert.Equal("1312-D", PlanLabels.CardText("1312-D", ""));
    }
}
