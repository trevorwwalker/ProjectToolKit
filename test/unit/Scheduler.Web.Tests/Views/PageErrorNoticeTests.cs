using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Tests.Views.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// MOVE-11: a failed page action is logged and shown at the top of the page for 10 minutes,
/// or until a later action succeeds.
/// </summary>
public sealed class PageErrorNoticeTests
{
    // MOVE-11
    [Fact]
    public void Moves_PageError_NoticeTenMinutesOrUntilSuccess()
    {
        var clock = new TestClock();
        var started = clock.Now;
        var log = new RecordingSchedulerLog();
        var notice = new PageNotice(clock, log);
        var renderer = new PageRenderer(notice: notice);
        var snapshot = ViewsFixture.Plain();

        notice.Failed("card 040-B: the answers worktree could not be reset");

        // Still up at 9:59, with the time it happened and what failed.
        clock.Now = started.Add(new TimeSpan(0, 9, 59));
        var shown = renderer.Render(snapshot, remote: false, controlsAllowed: true);
        Assert.Contains("class='notice'", shown, StringComparison.Ordinal);
        Assert.Contains("card 040-B: the answers worktree could not be reset", shown, StringComparison.Ordinal);
        Assert.Contains(started.ToString("HH:mm:ss"), shown, StringComparison.Ordinal);

        // Gone at 10:00.
        clock.Now = started.Add(new TimeSpan(0, 10, 0));
        var gone = renderer.Render(snapshot, remote: false, controlsAllowed: true);
        Assert.DoesNotContain("class='notice'", gone, StringComparison.Ordinal);

        // A later action that works removes it at once.
        clock.Now = started.AddMinutes(1);
        notice.Failed("card 050-C: the port was taken");
        Assert.Contains("the port was taken", renderer.Render(snapshot, false, true), StringComparison.Ordinal);
        notice.Succeeded();
        Assert.DoesNotContain("class='notice'", renderer.Render(snapshot, false, true), StringComparison.Ordinal);

        // Every failure is logged; clearing the notice is not a log line.
        Assert.Equal(2, log.Lines.Count);
        Assert.Equal("card 040-B: the answers worktree could not be reset", log.Lines[0]);
        Assert.Equal("card 050-C: the port was taken", log.Lines[1]);
    }
}
