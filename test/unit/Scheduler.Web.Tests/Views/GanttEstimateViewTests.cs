using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1144-C: the schedule ahead on the Gantt (VW-19, gantt_plan.md 5d): estimated bars, arrows and Details.</summary>
public sealed class GanttEstimateViewTests
{
    // Sunday 12:00. The 24 h window is [now - 24 h, now + 3 h], 27 hours wide, so an hour is 100/27 % of it.
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static ViewCard Card(string id, ViewFolder folder, string status = "", int? box = null, params string[] dependsOn) => new()
    {
        Id = id,
        Number = long.Parse(id[..id.IndexOf('-')]),
        Category = id[(id.IndexOf('-') + 1)..],
        Title = "Title of " + id,
        Folder = folder,
        Status = status,
        TimeBoxMinutes = box,
        Worker = "Opus",
        DependsOn = dependsOn,
    };

    private static ViewRun Run(string card, double startH, double endH) => new()
    {
        CardId = card,
        Start = Now.AddHours(startH),
        End = Now.AddHours(endH),
        Result = "done",
        Model = "claude-opus-5-5",
        WorkerType = "Opus",
    };

    private static PageSnapshot Page(string project) => new()
    {
        Built = Now,
        Project = project,
        Version = "3.1.1",
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
        Health = new PageHealthView { Green = true, Text = "main green" },
        Stats = new PageStats { Cards = 0, Minutes = 0, Text = "Last 24 h: none" },
    };

    // 1-A done three hours ago; 2-A (1 h box) after it starts now; 3-A (4 h box) after 2-A runs past the window's
    // end; 4-B waits on you, 5-B on 4-B; 7-C ran in the window after 8-C, which finished days before it.
    private static PoolPageSnapshot Sample()
    {
        var cards = new[]
        {
            Card("1-A", ViewFolder.Done),
            Card("2-A", ViewFolder.Queue, "ready", 60, "1-A"),
            Card("3-A", ViewFolder.Queue, "ready", 240, "2-A"),
            Card("4-B", ViewFolder.Queue, "needs-human: which colour?"),
            Card("5-B", ViewFolder.Queue, "ready", 30, "4-B"),
            Card("7-C", ViewFolder.Done, dependsOn: "8-C"),
            Card("8-C", ViewFolder.Done),
        };
        var runs = new[] { Run("1-A", -6, -3), Run("7-C", -2, -1), Run("8-C", -60, -59) };
        var views = new ProjectViews
        {
            Project = "AmigaX",
            Snapshot = ViewsSnapshot.Build("AmigaX", cards, [], runs: runs),
            DoneLoaded = true,
        };
        return new PoolPageSnapshot
        {
            Built = Now,
            MaxWorkers = 1,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 1, WeightShare = 1, UsedShare = 1 }],
            Pages = [Page("AmigaX")],
            Views = [views],
        };
    }

    private static string Gantt(string range = "24h") =>
        new CombinedPageRenderer(theme: "slate").Render(Sample(), remote: false, controlsAllowed: true, "gantt?range=" + range);

    private static string Details(string key)
    {
        var html = Gantt();
        var at = html.IndexOf("data-gdet='" + key + "'", StringComparison.Ordinal);
        Assert.True(at >= 0, "a Details block for " + key);
        var end = html.IndexOf("class='gdet'", at, StringComparison.Ordinal);
        return end < 0 ? html[at..] : html[at..end];
    }

    // The HTML of one card row of the Gantt.
    private static string Row(string html, string id)
    {
        var at = html.IndexOf("data-gd='AmigaX|category|card:" + id + "'", StringComparison.Ordinal);
        Assert.True(at >= 0, "a row for " + id);
        var end = html.IndexOf("class='grow", at, StringComparison.Ordinal);
        return end < 0 ? html[at..] : html[at..end];
    }

    [Fact]
    public void QueuedCard_GetsADashedHatchedBar_AtItsEstimatedStart_LabelledEstAndModel()
    {
        var row = Row(Gantt(), "2-A");

        // Start now (24/27 of the window), one hour long (1/27).
        Assert.Contains("<rect class='gest' x='88.889' y='1.5' width='3.704' height='7' fill='url(#ghatch)' stroke='var(--muted)' stroke-dasharray='3 2'", row, StringComparison.Ordinal);
        Assert.Contains(">est. Opus</span>", row, StringComparison.Ordinal);
        // Its tooltip gives the basis, and what it waited for: an unfinished dependency, not a done one.
        Assert.Contains("<title>estimated Sun 12:00-Sun 13:00, 60 min (time box)</title>", row, StringComparison.Ordinal);
        Assert.Contains("<title>estimated Sun 13:00-Sun 17:00, 240 min (time box); waited for 2-A</title>", Row(Gantt(), "3-A"), StringComparison.Ordinal);
        Assert.DoesNotContain("gb-", row, StringComparison.Ordinal);   // never coloured as a result
        Assert.Contains("<pattern id='ghatch'", Gantt(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dependency_IsAnArrow_FromItsLastBarToTheDependentsFirstBar()
    {
        var html = Gantt();

        // 1-A finished at now - 3 h (21/27 = 77.778 %), 2-A starts at now (88.889 %).
        Assert.Contains("<g class='garrow' data-from='1-A' data-to='2-A'><title>after 1-A</title><path d='M77.778,0 L77.778,5 L88.889,5'", Row(html, "2-A"), StringComparison.Ordinal);
        // An estimate's end is where the next card's arrow starts: 2-A ends at now + 1 h (92.593 %).
        Assert.Contains("<g class='garrow' data-from='2-A' data-to='3-A'><title>after 2-A</title><path d='M92.593,0 L92.593,5 L92.593,5'", Row(html, "3-A"), StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyOutsideTheWindow_IsAStubWithItsId()
    {
        var row = Row(Gantt(), "7-C");

        Assert.Contains("<g class='garrow gstub' data-from='8-C' data-to='7-C'>", row, StringComparison.Ordinal);
        Assert.Contains("'>8-C</span>", row, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingCards_ShowAStubAtNow_SayingWhatTheyWaitOn()
    {
        var html = Gantt();

        Assert.Contains("<rect class='gest gwait' x='88.889'", Row(html, "4-B"), StringComparison.Ordinal);
        Assert.Contains(">waiting on you</span>", Row(html, "4-B"), StringComparison.Ordinal);
        Assert.Contains(">waiting on 4-B</span>", Row(html, "5-B"), StringComparison.Ordinal);
        Assert.DoesNotContain("<rect class='gest' ", Row(html, "5-B"), StringComparison.Ordinal);
    }

    [Fact]
    public void EstimatesPastTheRange_KeepTheRange_AndShowMoreAhead()
    {
        var html = Gantt();

        // 3-A ends at now + 5 h, past the window's now + 3 h.
        Assert.Contains("class='gmore'", html, StringComparison.Ordinal);
        Assert.Contains(">more ahead -&gt; Sun 17:00</span>", html, StringComparison.Ordinal);

        // The week's range ends most of a day after it: no marker.
        Assert.DoesNotContain("class='gmore'", Gantt("1w"), StringComparison.Ordinal);
    }

    [Fact]
    public void Details_GiveACardsEstimate_AndWhatItWaitsFor_AndAGroupsEstimatedFinish()
    {
        var card = Details("AmigaX|category|card:3-A");
        Assert.Contains("<tr><th>Estimate</th><td>estimated start Sun 13:00 end Sun 17:00 <span class='mute'>(time box)</span></td></tr>", card, StringComparison.Ordinal);
        Assert.Contains("<tr><th>Waits for</th><td><a href='/card?project=AmigaX&id=2-A'>2-A</a></td></tr>", card, StringComparison.Ordinal);

        Assert.Contains("<tr><th>Estimate</th><td><span class='mute'>waiting on 4-B</span></td></tr>", Details("AmigaX|category|card:5-B"), StringComparison.Ordinal);
        Assert.Contains("<div class='gdest'>estimated finish Sun 17:00</div>", Details("AmigaX|category|category:A"), StringComparison.Ordinal);
        Assert.DoesNotContain("gdest", Details("AmigaX|category|category:C"), StringComparison.Ordinal);
    }
}
