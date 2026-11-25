// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>The views card's estimate fields, rules VW-18 and VW-19 (card 1141-A).</summary>
public class ViewCardEstimateFieldsTests
{
    private static Card Open(string text) => CardFiles.ParseText(
        new Card { Folder = CardFolder.Queue, Path = "jobs/queue/5-A-x.md", Name = "5-A-x", Num = 5, Category = "A", Id = "5-A" },
        text);

    private static DoneCard Done(string text) => DoneHeaders.Parse(
        new DoneCard { Folder = "done", Path = "jobs/done/5-A-x.md", Name = "5-A-x", Num = 5, Category = "A", Id = "5-A" },
        text,
        "blob");

    private const string Full =
        "# 5-A: x\n\nCategory: A\nStatus: ready\nDepends on: 1129-C, 1131-B\nScope: a/b.cs, c/, test/d/\nTime box: 120 min\n\n## Task\n";

    [Fact]
    public void VW_19_OpenCard_FillsDependsTimeBoxAndScope()
    {
        var v = ViewCard.From(Open(Full));
        Assert.Equal(["1129-C", "1131-B"], v.DependsOn);
        Assert.Equal(120, v.TimeBoxMinutes);
        Assert.Equal(["a/b.cs", "c/", "test/d/"], v.Scope);
    }

    [Fact]
    public void VW_19_DoneCard_FillsTheSameFields()
    {
        var v = ViewCard.From(Done(Full));
        Assert.Equal(["1129-C", "1131-B"], v.DependsOn);
        Assert.Equal(120, v.TimeBoxMinutes);
        Assert.Equal(["a/b.cs", "c/", "test/d/"], v.Scope);
    }

    [Fact]
    public void VW_19_DependsNone_AndNoTimeBoxOrScope_GiveEmptyAndNull()
    {
        var v = ViewCard.From(Open("# 5-A: x\n\nStatus: ready\nDepends on: none (it stands alone)\n"));
        Assert.Empty(v.DependsOn);
        Assert.Null(v.TimeBoxMinutes);
        Assert.Empty(v.Scope);
    }

    [Fact]
    public void VW_19_Version_MovesWithWorkers_AndTypicals()
    {
        var a = ViewsSnapshot.Build("p", [], []);
        var b = ViewsSnapshot.Build("p", [], [], workers: 3);
        var c = ViewsSnapshot.Build("p", [], [], typicals: [new AreaTypical { Area = "A", Model = "Sonnet", Runs = 3, Median = 20, P90 = 40 }]);
        Assert.Equal(1, a.Workers);
        Assert.Equal(3, b.Workers);
        Assert.Single(c.Typicals);
        Assert.NotEqual(a.Version, b.Version);
        Assert.NotEqual(a.Version, c.Version);
        Assert.Equal(a.Version, ViewsSnapshot.Build("p", [], []).Version);
    }

    [Fact]
    public void VW_19_Version_MovesWithACardsDependsTimeBoxAndScope()
    {
        var plain = ViewCard.From(Open("# 5-A: x\n\nStatus: ready\n"));
        var full = ViewCard.From(Open(Full));
        Assert.NotEqual(ViewsSnapshot.Build("p", [plain], []).Version, ViewsSnapshot.Build("p", [full], []).Version);
    }
}
