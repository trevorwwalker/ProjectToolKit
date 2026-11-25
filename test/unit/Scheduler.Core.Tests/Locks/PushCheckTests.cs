// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Locks;

/// <summary>
/// Dynamic locks, the push check (design <c>docs/design/tdd/scheduler/dynamic-locks.md</c>, card 1017-A):
/// section 7's Push check (DL-15 to DL-18). Pure.
/// </summary>
public class PushCheckTests
{
    private const string Card = "1017-A-dl-push-check-audit";

    private const string Exe = @"C:\ptk\scheduler-next.exe";

    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] AddOnly = ["docs/known_gaps.md", "code/src/Directory.Build.props"];

    private static CardLocks? Held(params string[] paths) =>
        LockTable.Empty("C:/repo").Start(Card, new CardAge(1, 1017), Scope.Of(paths), T0).Find(Card);

    private static PushCheckResult Check(params PushCheckPath[] paths) => PushCheck.Check(Request(paths));

    private static PushCheckRequest Request(params PushCheckPath[] paths) => new()
    {
        Card = Card,
        Ref = "refs/heads/main",
        Paths = paths,
        Locks = Held("code/src/Scheduler.Core/Locks/PushCheck*.cs"),
        AddOnly = AddOnly,
        Exe = Exe,
    };

    private static PushCheckPath P(string path, int added = 1, int deleted = 0, bool isNew = false) => new(path, isNew, added, deleted);

    [Fact]
    public void DL15_OwnCardFile_IsAllowed_InAnyFolder()
    {
        Assert.True(Check(P($"jobs/active/{Card}.md"), P($"jobs/done/{Card}.md")).Ok);
    }

    [Fact]
    public void DL15_NewFileUnderRequests_IsAllowed()
    {
        Assert.True(Check(P("jobs/requests/0042-question.md", isNew: true)).Ok);
    }

    [Fact]
    public void DL15_ChangedExistingRequest_IsRefused()
    {
        var r = Check(P("jobs/requests/README.md"));
        Assert.Equal(["jobs/requests/README.md"], r.Refused);
    }

    [Fact]
    public void DL15_AddOnlyFile_IsAllowed()
    {
        Assert.True(Check(P("docs/known_gaps.md", added: 3)).Ok);
    }

    [Fact]
    public void DL15_AnotherCardsCardFile_IsRefused_EvenUnderAHeldFolder()
    {
        var req = Request(P("jobs/queue/990-B-other.md")) with { Locks = Held("jobs/") };
        var r = PushCheck.Check(req);
        Assert.Equal(["jobs/queue/990-B-other.md"], r.Refused);
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData("jobs/card-template.md")]
    [InlineData("jobs/attachments/x.md")]
    [InlineData("jobs/README.md")]
    [InlineData("jobs/requests/README.md")]
    public void DL15_OrdinaryJobsPath_IsAllowedWhenHeld_AndRefusedWithTheLockCommandWhenNot(string path)
    {
        var held = Request(P(path)) with { Locks = Held("jobs/card-template.md", "jobs/attachments/", "jobs/README.md", "jobs/requests/README.md") };
        Assert.True(PushCheck.Check(held).Ok);

        var r = Check(P(path));
        Assert.Equal([path], r.Refused);
        Assert.Contains($"lock {path}", r.Lines[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jobs/queue/990-B-other.md")]
    [InlineData("jobs/needs-human/990-B-other.md")]
    [InlineData("jobs/plans/x.md")]
    [InlineData("jobs/archive/x.md")]
    [InlineData("jobs/.logs/x")]
    [InlineData("jobs/.planner-outbox/x.json")]
    [InlineData("jobs/requests/answered/x.md")]
    public void DL15_ProtectedJobsPath_IsRefused_EvenWhenHeld(string path)
    {
        var r = PushCheck.Check(Request(P(path, isNew: true)) with { Locks = Held("jobs/") });
        Assert.Equal([path], r.Refused);
        Assert.Contains("scheduler's", r.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DL18_HeldPath_IsAllowed()
    {
        var r = Check(P("code/src/Scheduler.Core/Locks/PushCheck.cs"));
        Assert.True(r.Ok);
        Assert.Empty(r.Lines);
    }

    [Fact]
    public void DL18_UnheldPath_IsRefused_NamingItAndTheLockCommand()
    {
        var r = Check(P("code/src/Scheduler.Core/Locks/PushCheck.cs"), P("docs/design/x.md"));
        Assert.Equal(["docs/design/x.md"], r.Refused);
        var line = Assert.Single(r.Lines);
        Assert.Contains($"{Card} does not hold docs/design/x.md", line, StringComparison.Ordinal);
        Assert.Contains($"{Exe} lock docs/design/x.md --why", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DL18_BackslashPaths_AreCheckedAsSlashes()
    {
        Assert.True(Check(P(@"code\src\Scheduler.Core\Locks\PushCheck.cs")).Ok);
    }

    [Fact]
    public void DL18_CardWithNoHeldSet_IsRefusedEveryLockedPath()
    {
        var r = PushCheck.Check(Request(P("code/src/a.cs"), P("docs/known_gaps.md")) with { Locks = null });
        Assert.Equal(["code/src/a.cs"], r.Refused);
    }

    [Theory]
    [InlineData("refs/heads/wip/1017-A")]
    [InlineData("wip/1017-A")]
    [InlineData("refs/heads/feature")]
    public void DL18_PushToAnotherRef_IsNeverRefused(string target)
    {
        var r = PushCheck.Check(Request(P("docs/design/x.md")) with { Ref = target });
        Assert.True(r.Ok);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("refs/heads/main")]
    public void DL18_PushToMain_IsChecked(string target)
    {
        Assert.False(PushCheck.Check(Request(P("docs/design/x.md")) with { Ref = target }).Ok);
    }

    [Theory]
    [InlineData("3.0.51")]
    [InlineData("3.0.50")]
    [InlineData("2.9.99")]
    public void DL17_VersionLoweredOrEqual_IsRefused(string pushed)
    {
        var r = PushCheck.Check(Request(P("code/src/Directory.Build.props", 1, 1)) with { MainVersion = "3.0.51", PushedVersion = pushed });
        Assert.False(r.Ok);
        Assert.Contains(r.Lines, l => l.Contains("bump again from main's version", StringComparison.Ordinal));
        Assert.Empty(r.Warnings);
    }

    [Theory]
    [InlineData("3.0.52")]
    [InlineData("3.1.0")]
    public void DL17_VersionRaised_IsAllowed_AndItsLineIsNoWarning(string pushed)
    {
        var r = PushCheck.Check(Request(P("code/src/Directory.Build.props", 1, 1)) with { MainVersion = "3.0.51", PushedVersion = pushed });
        Assert.True(r.Ok);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void DL17_UnreadablePushedVersion_IsRefused()
    {
        var r = PushCheck.Check(Request(P("code/src/Directory.Build.props", 1, 1)) with { MainVersion = "3.0.51", PushedVersion = "three" });
        Assert.False(r.Ok);
    }

    [Fact]
    public void DL16_AddOnlyFileWithDeletedLines_IsAWarning_NotARefusal()
    {
        var r = Check(P("docs/known_gaps.md", added: 2, deleted: 2));
        Assert.True(r.Ok);
        var w = Assert.Single(r.Warnings);
        Assert.Contains("docs/known_gaps.md", w, StringComparison.Ordinal);
        Assert.Contains("2", w, StringComparison.Ordinal);
    }

    [Fact]
    public void DL16_VersionFileDeletingMoreThanTheVersionLine_IsAWarning()
    {
        var r = PushCheck.Check(Request(P("code/src/Directory.Build.props", 2, 2)) with { MainVersion = "3.0.51", PushedVersion = "3.0.52" });
        Assert.True(r.Ok);
        Assert.Single(r.Warnings);
    }
}
