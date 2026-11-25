// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>VW-24: in a plan with numbered "##" headings only those are steps (card 1230-A).</summary>
public class PlanStepsNumberedTests
{
    [Fact]
    public void VW24_NumberedPlan_UnnumberedHeadingsAreNotSteps()
    {
        var steps = PlanSteps.Of("## Decisions\n\n## Open questions\n\n## 1. A\n\n## 2. B\n");

        Assert.Equal(["1", "2"], steps.Select(s => s.Label));
        Assert.Equal(["1. A", "2. B"], steps.Select(s => s.Title));
    }

    [Fact]
    public void VW24_NumberedPlan_SubHeadingUnderUnnumberedIsDropped()
    {
        var steps = PlanSteps.Of("## 1. A\n\n## Notes\n\n### x\n\n## 2. B\n");

        Assert.Equal(["1", "2"], steps.Select(s => s.Label));
        Assert.Empty(steps[0].SubSteps);
        Assert.Empty(steps[1].SubSteps);
    }

    [Fact]
    public void VW24_NumberedPlan_SubStepsKeepTheirNumbers()
    {
        var steps = PlanSteps.Of("## 2. B\n\n### One\n\n### 2.5 Two\n");

        Assert.Equal(["2.1", "2.5"], steps.Single().SubSteps.Select(s => s.Label));
    }

    [Fact]
    public void VW24_NumberedPlan_KeepsZero()
    {
        var steps = PlanSteps.Of("## 0. Today\n\n## 1. Model\n");

        Assert.Equal(["0", "1"], steps.Select(s => s.Label));
    }

    [Fact]
    public void VW24_UnnumberedPlan_KeepsOrderCounting()
    {
        var steps = PlanSteps.Of("## Today\n\n### a\n\n## Steps\n\n## Checks\n");

        Assert.Equal(["1", "2", "3"], steps.Select(s => s.Label));
        Assert.Equal(["1.1"], steps[0].SubSteps.Select(s => s.Label));
    }

    [Fact]
    public void VW24_HeadingsInCodeFencesAreIgnored()
    {
        var steps = PlanSteps.Of("## Notes\n\n```\n## 7. Fake\n```\n\n## 1. A\n\n~~~\n## 8. Fake\n~~~\n");

        Assert.Equal(["1"], steps.Select(s => s.Label));
        Assert.Equal(["1. A"], steps.Select(s => s.Title));
    }
}
