using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 610-C item 5 (the developer, 2026-11-18): the Control window's body no longer repeats the
/// state in a "State: running&nbsp;&nbsp;Health: ..." line, because 520-A put the state in the
/// window's title bar. The health joins it there, next to the state, each part keeping its colour,
/// and the live refresh keeps it current; the link to <c>/health</c> comes with it.
/// </summary>
public sealed class PageControlTitleTests
{
    private const string GreenHealth = "main 1a2b3c4 green at 12:20";

    // PAGE-5 (610-C) · the body line is gone: neither "State:" nor "Health:" is in the Control
    // window's body any more.
    [Fact]
    public void Control_Body_NoStateOrHealthLine()
    {
        var html = Render(ViewsFixture.Plain() with
        {
            Health = new PageHealthView { Green = true, Sha = "1a2b3c4", Text = GreenHealth, HasLog = true },
        });

        Assert.DoesNotContain("State:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Health:", html, StringComparison.Ordinal);
    }

    // PAGE-5, PAGE-8 (610-C) · the Control title carries the state and the health, in their own
    // colours, ahead of the worker count: "ProjectToolkit Scheduler v<the version> - running, main 1a2b3c4
    // green at 12:20   2 workers".
    [Fact]
    public void Control_Title_CarriesStateAndHealth()
    {
        var html = Render(ViewsFixture.Full() with
        {
            Health = new PageHealthView { Green = true, Sha = "1a2b3c4", Text = GreenHealth },
        });

        Assert.Contains(
            "<span class='ok'>running</span>, <span class='ok'>main 1a2b3c4 green at 12:20</span>",
            html,
            StringComparison.Ordinal);

        // ...and the health is part of the Control window's title bar, before the worker count.
        var bar = html[html.IndexOf("id='w-control'", StringComparison.Ordinal)..];
        var state = bar.IndexOf("<span class='ok'>running</span>", StringComparison.Ordinal);
        var health = bar.IndexOf(GreenHealth, StringComparison.Ordinal);
        var workers = bar.IndexOf(" workers", StringComparison.Ordinal);
        Assert.True(state >= 0 && health > state && workers > health, "the title reads state, health, workers");
    }

    // PAGE-5 (610-C) · before the first health check the health part is muted, not green: nothing
    // has been proved yet.
    [Fact]
    public void Control_Title_HealthNotCheckedIsMuted()
    {
        var html = Render(ViewsFixture.Plain() with { Health = new PageHealthView { Text = "main not checked yet" } });

        Assert.Contains("<span class='mute'>main not checked yet</span>", html, StringComparison.Ordinal);
    }

    // PAGE-5, HLTH-6 (610-C) · a red main is shown red in the title, and keeps the link to its log;
    // the log link only appears when there is a log to show.
    [Fact]
    public void Control_Title_RedHealthLinksItsLog()
    {
        var red = Render(ViewsFixture.Plain() with
        {
            Health = new PageHealthView { Green = false, Sha = "1a2b3c4", Text = "main 1a2b3c4 RED at 12:20", HasLog = true },
        });

        Assert.Contains("<span class='bad'>main 1a2b3c4 RED at 12:20</span>", red, StringComparison.Ordinal);
        Assert.Contains("<a href='/health'>log</a>", red, StringComparison.Ordinal);

        var noLog = Render(ViewsFixture.Plain() with
        {
            Health = new PageHealthView { Green = false, Sha = "1a2b3c4", Text = "main 1a2b3c4 RED at 12:20" },
        });
        Assert.DoesNotContain("<a href='/health'>log</a>", noLog, StringComparison.Ordinal);
    }

    // PAGE-8 (610-C) · the state keeps its own colour beside the health: a paused scheduler is amber
    // whatever main's health says.
    [Fact]
    public void Control_Title_StateKeepsItsOwnColour()
    {
        var html = Render(ViewsFixture.Plain(SchedulerState.Paused) with
        {
            Health = new PageHealthView { Green = false, Sha = "1a2b3c4", Text = "main 1a2b3c4 RED at 12:20" },
        });

        Assert.Contains("<span class='warn'>paused</span>, <span class='bad'>main 1a2b3c4 RED at 12:20</span>", html, StringComparison.Ordinal);
    }

    // PAGE-5 (610-C) · a health line that holds markup is still only text in the title.
    [Fact]
    public void Control_Title_HealthTextIsEncoded()
    {
        var html = Render(ViewsFixture.Plain() with { Health = new PageHealthView { Text = "main <b>red</b> & down" } });

        Assert.Contains("<span class='mute'>main &lt;b&gt;red&lt;/b&gt; &amp; down</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("main <b>red</b>", html, StringComparison.Ordinal);
    }

    // PAGE-5 (610-C) · what the health line used to carry besides the state stays in the body: the
    // model servers that do not answer, and the category overlaps.
    [Fact]
    public void Control_Body_KeepsServerDownAndOverlaps()
    {
        var html = Render(ViewsFixture.Plain() with
        {
            Health = new PageHealthView
            {
                Green = true,
                Text = GreenHealth,
                DownServers = ["http://localhost:1"],
                CategoryOverlaps = ["lane A and lane C both own code/src/Shared/"],
            },
        });

        Assert.Contains("model server http://localhost:1 does not answer", html, StringComparison.Ordinal);
        Assert.Contains("Category overlap: lane A and lane C both own code/src/Shared/", html, StringComparison.Ordinal);
    }

    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);
}
