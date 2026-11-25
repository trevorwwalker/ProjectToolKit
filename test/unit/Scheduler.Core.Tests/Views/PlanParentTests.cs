// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>VW-25 and VW-26: reading a plan's Parent line and resolving it (card 1232-A).</summary>
public class PlanParentTests
{
    [Fact]
    public void VW25_Parent_ReadFromHeader()
    {
        var line = PlanParent.Of("# T\n\nParent: jobs/plans/a_plan.md, step 2\n\n## 1. Go\n");

        Assert.Equal(new PlanParentLine("jobs/plans/a_plan.md", "step 2"), line);
    }

    [Fact]
    public void VW25_Parent_AfterFirstHeadingIsIgnored() =>
        Assert.Null(PlanParent.Of("# T\n\n## 1. Go\n\nParent: jobs/plans/a_plan.md, step 2\n"));

    [Fact]
    public void VW25_Parent_FirstLineWins()
    {
        var line = PlanParent.Of("# T\nParent: jobs/plans/a_plan.md, step 1\nParent: jobs/plans/b_plan.md, step 2\n## 1. X\n");

        Assert.Equal("jobs/plans/a_plan.md", line?.Path);
    }

    [Fact]
    public void VW25_Parent_CaseBoldAndBackticks()
    {
        var line = PlanParent.Of("# T\n**parent:** `jobs/plans/a_plan.md`, Step 2\n");
        var other = PlanParent.Of("# T\n**Parent**: jobs\\plans\\a_plan.md; step 3\n");

        Assert.Equal(new PlanParentLine("jobs/plans/a_plan.md", "Step 2"), line);
        Assert.Equal(new PlanParentLine("jobs/plans/a_plan.md", "step 3"), other);
    }

    [Fact]
    public void VW25_Parent_NoneOrEmptyIsRoot()
    {
        Assert.Null(PlanParent.Of("# T\nParent: none\n## 1. X\n"));
        Assert.Null(PlanParent.Of("# T\nParent:\n## 1. X\n"));
        Assert.Null(PlanParent.Of("# T\nno parent here\n"));
        Assert.Null(PlanParent.Of(null));
    }

    [Fact]
    public void VW25_Parent_StepTextKept() =>
        Assert.Equal("step 2.4", PlanParent.Of("# T\nParent: jobs/plans/a_plan.md, step 2.4\n")?.StepText);

    private const string Parent = "# P\n\n## 1. One\n\n### 1.1 Sub\n\n## 2. Two\n";

    private static PlanForest Forest(string childParentLine)
    {
        var texts = new Dictionary<string, string>
        {
            ["jobs/plans/a_plan.md"] = Parent,
            ["jobs/plans/b_plan.md"] = "# B\n" + childParentLine + "\n## 1. Do\n",
        };
        var onMain = texts.Keys.ToHashSet();
        return PlanTree.Nest([], onMain, texts, onMain.Select(p => new PlanRef(PlanKind.Plans, p)));
    }

    [Fact]
    public void VW26_OldPath_ResolvesAsVW12()
    {
        var forest = Forest("Parent: docs/jobs/plans/a_plan.md, step 2");

        var root = Assert.Single(forest.Top);
        Assert.Equal("jobs/plans/b_plan.md", Assert.Single(root.Steps[1].Plans).Plan.Path);
    }

    [Fact]
    public void VW26_SubStep_Picked()
    {
        var root = Assert.Single(Forest("Parent: jobs/plans/a_plan.md, step 1.1").Top);

        Assert.Single(root.Steps[0].SubSteps[0].Plans);
        Assert.Empty(root.Steps[0].Plans);
    }

    [Fact]
    public void VW26_SubStepMissing_FallsBackToStep()
    {
        var root = Assert.Single(Forest("Parent: jobs/plans/a_plan.md, step 2.9").Top);

        Assert.Single(root.Steps[1].Plans);
    }
}
