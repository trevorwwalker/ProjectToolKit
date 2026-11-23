using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1022-C: the pool page's holds, waits, yielded notes and day counts (dynamic-locks DL-24 to DL-26).</summary>
public sealed class PoolLockPageTests
{
    private static readonly PageStateView Running = new() { State = SchedulerState.Running, Text = "running" };

    private static PageSnapshot Snapshot(PageHolds? holds, string yielded = "") => new()
    {
        Project = "P",
        Built = DateTimeOffset.UnixEpoch,
        State = Running,
        Slots = [new PageSlotView { Slot = 1, Card = "1022-C-x.md", Holds = holds }],
        Queue = [new PageQueueItem { Name = "1022-C-x.md", Title = "t", Yielded = yielded }],
    };

    private static string Html(PageSnapshot snapshot, PageLockCounts? counts = null) =>
        new CombinedPageRenderer(() => PageThemes.BuiltIn, "slate", () => []).Render(
            new PoolPageSnapshot
            {
                Built = DateTimeOffset.UnixEpoch,
                Pages = [snapshot],
                Projects = [new PoolProjectView { Name = "P", LockCounts = counts }],
            },
            remote: false,
            controlsAllowed: true);

    // DL-24: claim on one line, granted entries on another, each with its age; in the window and the Queue row.
    [Fact]
    public void Holds_ClaimAndAlso_OnTwoLinesInWindowAndQueueRow()
    {
        var holds = new PageHolds
        {
            Claim = ["code/src/Scheduler.Web/Views/CombinedPageRenderer.cs"],
            ClaimMinutes = 12,
            Also = [new PageHeldEntry("docs/known_gaps.md", 3)],
        };
        var html = Html(Snapshot(holds));

        Assert.Equal(2, Count(html, "<div>holds: code/src/Scheduler.Web/Views/CombinedPageRenderer.cs (12 min)</div>"));
        Assert.Equal(2, Count(html, "<div>also holds: docs/known_gaps.md (3 min)</div>"));
    }

    // DL-25: the wait line, and the yielded note in the Queue.
    [Fact]
    public void Waits_AndYielded_Shown()
    {
        var wait = "waits for lock on vm/68000.cpp held by 5-A-x since 10:22, gives up at 10:42";
        var html = Html(Snapshot(new PageHolds { Claim = ["a"], Wait = wait }, "yielded: waits for vm/68000.cpp held by 5-A-x"));

        Assert.Contains(wait, html, StringComparison.Ordinal);
        Assert.Contains("<span class='yielded'>yielded: waits for vm/68000.cpp held by 5-A-x</span>", html, StringComparison.Ordinal);
    }

    // DL-26: the day's counts in the Projects window.
    [Fact]
    public void Counts_ShownInProjectsWindow()
    {
        var html = Html(Snapshot(null), new PageLockCounts { Grants = 4, Waits = 2, Yields = 1, Refusals = 3, PushesRefused = 5, Strays = 6 });

        Assert.Contains("Locks today: 4 granted, 2 waited, 1 yielded, 3 refused, 5 pushes refused, 6 strays", html, StringComparison.Ordinal);
    }

    // Locks off: no holds, no counts, nothing on the page.
    [Fact]
    public void LocksOff_NothingShown()
    {
        var html = Html(Snapshot(null));

        Assert.DoesNotContain("holds:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Locks today", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class='yielded'", html, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        var n = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
