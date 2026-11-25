// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 510-B: the page elements 480-C left waiting for Host's facts, now drawn from the snapshot's
/// new members, and the developer's toggles (2026-11-18): Planner push and Pause show their state.
/// </summary>
public sealed class PageHostGapTests
{
    private static string Render(PageSnapshot snapshot, bool controlsAllowed = true) =>
        new PageRenderer().Render(snapshot, remote: !controlsAllowed, controlsAllowed);

    private static PageSlotView Running(int slot = 1) => new()
    {
        Slot = slot,
        WorkerName = "w1",
        Model = "claude-opus-5-5",
        Card = "120-B-host.md",
        CardTitle = "Host the things",
        Start = ViewsFixture.Built.AddMinutes(-30),
        TimeLimit = 60,
        CategoryName = "host",
    };

    // The part of the page that is the named window.
    private static string WindowOf(string html, string marker)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {marker}");
        var start = html.LastIndexOf("<section class='win'", at, StringComparison.Ordinal);
        var end = html.IndexOf("</section>", at, StringComparison.Ordinal);
        return html[start..end];
    }

    // Planner push on: the button reads "Planner push: on" and looks pressed; no separate line.
    [Fact]
    public void Control_PlannerPushOn_PressedButton()
    {
        var html = Render(ViewsFixture.Plain() with { PlannerLine = "Planner push: on", PlannerPush = true });

        Assert.Contains("<button class='pressed ok' aria-label='Planner push: on.", html, StringComparison.Ordinal);
        Assert.Contains(">Planner push: on</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class='mute'>Planner push: on</div>", html, StringComparison.Ordinal);
        Assert.Contains("action='/planner-toggle'", html, StringComparison.Ordinal);
    }

    // Planner push off: a normal button reading "Planner push: off"; no separate line.
    [Fact]
    public void Control_PlannerPushOff_NormalButton()
    {
        var html = Render(ViewsFixture.Plain() with { PlannerLine = "Planner push: off", PlannerPush = false });

        Assert.Contains("<button class='' aria-label='Planner push: off.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class='mute'>Planner push: off</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pressed", WindowOf(html, $"ProjectToolkit Scheduler v{ViewsFixture.ShownVersion}"), StringComparison.Ordinal);
    }

    // No planner outbox: no Planner push button at all.
    [Fact]
    public void Control_NoPlanner_NoButton()
    {
        Assert.DoesNotContain("/planner-toggle", Render(ViewsFixture.Plain()), StringComparison.Ordinal);
    }

    // Running: a normal Pause button that pauses.
    [Fact]
    public void Control_Running_PauseButton()
    {
        var html = Render(ViewsFixture.Plain(SchedulerState.Running));

        Assert.Contains("action='/pause'", html, StringComparison.Ordinal);
        Assert.Contains("<button class='' aria-label='Pause.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Paused</button>", html, StringComparison.Ordinal);
    }

    // Any paused state: the button reads "Paused", looks pressed in the warn colour, and resumes.
    [Theory]
    [InlineData(SchedulerState.Paused)]
    [InlineData(SchedulerState.PausedRed)]
    [InlineData(SchedulerState.PausedQuota)]
    public void Control_Paused_PressedButtonResumes(SchedulerState state)
    {
        var html = Render(ViewsFixture.Plain(state));

        Assert.Contains("<form method='post' action='/resume'", html, StringComparison.Ordinal);
        Assert.Contains("<button class='pressed warn' aria-label='Paused.", html, StringComparison.Ordinal);
        Assert.Contains(">Paused</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action='/pause'", html, StringComparison.Ordinal);
    }

    // The pressed style is in the CSS every theme gets (PageThemes.CommonCss, on every page).
    [Fact]
    public void Themes_PressedStyle_Common()
    {
        var css = Render(ViewsFixture.Plain());
        Assert.Contains("button.pressed {", css, StringComparison.Ordinal);
        Assert.Contains("button.pressed.ok { color: var(--ok); }", css, StringComparison.Ordinal);
        Assert.Contains("button.pressed.warn { color: var(--warn); }", css, StringComparison.Ordinal);
    }

    // The time-limit bar: "n/m min" and the bar filled from the first draw; late at 90%.
    [Fact]
    public void Worker_TimeLimit_BarAndMinutes()
    {
        var html = Render(ViewsFixture.Plain() with { Slots = [Running()] });
        var win = WindowOf(html, "href='/log?slot=1'");

        Assert.Contains("<span class='tag' data-slot='1'>30/60 min</span>", win, StringComparison.Ordinal);
        Assert.Contains("<span class='prog' data-slot='1' style='width:50%'></span>", win, StringComparison.Ordinal);

        var late = Render(ViewsFixture.Plain() with { Slots = [Running() with { TimeLimit = 32 }] });
        Assert.Contains("<span class='prog late' data-slot='1' style='width:93%'></span>", late, StringComparison.Ordinal);
    }

    // Stopping: the line says when it is killed.
    [Fact]
    public void Worker_Stopping_KilledAt()
    {
        var killAt = new DateTimeOffset(2026, 11, 18, 12, 45, 0, TimeSpan.Zero);
        var html = Render(ViewsFixture.Plain() with { Slots = [Running() with { StopWhy = "asked from the page", KillAt = killAt }] });

        Assert.Contains("<div class='warn'>Stopping: asked from the page. Killed at 12:45 if still running.</div>", html, StringComparison.Ordinal);
    }

    // The title: the model's short name (1146-A: no premium mark); the card link's tooltip, its title and category name.
    [Fact]
    public void Worker_Title_ShortNameCategoryNameAndCardTitle()
    {
        var html = Render(ViewsFixture.Plain() with { Slots = [Running() with { ModelShort = "Opus" }] });
        var win = WindowOf(html, "href='/log?slot=1'");

        Assert.Contains("w1 - slot 1 (Opus)", win, StringComparison.Ordinal);
        Assert.DoesNotContain("premium", win, StringComparison.Ordinal);
        // The category text left the title in 490-C; its name is in the card link's tooltip.
        Assert.Contains("<a class='cardlink' href='/card?name=120-B-host.md' title='Host the things - category B (host)'>120-B-host.md</a>", win, StringComparison.Ordinal);
    }

    // Without a short name the title keeps the model as configured.
    [Fact]
    public void Worker_Title_NoShortName_Model()
    {
        var html = Render(ViewsFixture.Plain() with { Slots = [Running()] });

        Assert.Contains("w1 - slot 1 (claude-opus-5-5)", html, StringComparison.Ordinal);
        Assert.DoesNotContain(", premium", html, StringComparison.Ordinal);
    }

    // Queue: the newest run's log link and the Depends on links.
    [Fact]
    public void Queue_LogAndDependsLinks()
    {
        var snapshot = ViewsFixture.Plain() with
        {
            Queue =
            [
                new PageQueueItem { Name = "100-A-x.md", Title = "first", Note = "next up" },
                new PageQueueItem { Name = "120-B-y.md", Title = "second", Note = "waiting on 100-A", Depends = ["100-A"], Log = @"C:\logs\120-B-20261118-101529.log" },
            ],
        };

        var queue = WindowOf(Render(snapshot), "<b>Queue</b>");

        Assert.Contains("<th>Log</th><th>Category</th><th>Depends on</th>", queue, StringComparison.Ordinal);
        Assert.Contains("<a href='/log?file=120-B-20261118-101529.log' title='The newest run&#39;s log'>log</a>", queue, StringComparison.Ordinal);
        Assert.Contains("<span class='dep' data-dep='100-A' title='first'>100-A</span>", queue, StringComparison.Ordinal);
    }

    // Recent events: worker, card link, result, minutes, log and Open in their own columns.
    [Fact]
    public void Events_Columns()
    {
        var snapshot = ViewsFixture.Plain() with
        {
            Events =
            [
                new PageEvent
                {
                    Time = ViewsFixture.Built,
                    Text = "slot 2  120-B-y: review, 17 min",
                    Slot = 2,
                    Card = "120-B-y",
                    Result = "review",
                    Minutes = 17,
                    Log = @"C:\logs\120-B-20261118-101529.log",
                },
            ],
        };

        var events = WindowOf(Render(snapshot), "<b>Recent events</b>");

        Assert.Contains("<th>When</th><th>Worker</th><th>Card</th><th>Result</th><th>Time</th>", events, StringComparison.Ordinal);
        // 968-C: the short columns (When, Worker, Result, Time) are marked never-to-wrap, the card is
        // the ellipsised one and carries the whole name in its tooltip.
        Assert.Contains("<tr data-card='120-B'><td class='nw'>11-18 12:30</td><td class='nw'>2</td><td class='card'><a href='/card?name=120-B-y' title='120-B-y'>120-B-y</a></td><td class='nw'><span class='ok'>review</span></td><td class='nw'>17 min</td>", events, StringComparison.Ordinal);
        Assert.Contains("/log?file=120-B-20261118-101529.log", events, StringComparison.Ordinal);
        Assert.Contains("action='/open?card=120-B-y'", events, StringComparison.Ordinal);
    }

    // Waiting on you: the card's title as its link's tooltip; a reason from the Result journal is marked.
    [Fact]
    public void Waiting_TitleAndResultMarker()
    {
        var snapshot = ViewsFixture.Plain() with
        {
            Waiting = [new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "070-A-x", Title = "Banner", Note = "Result: look at it", NoteFrom = "result" }],
        };

        var html = Render(snapshot);

        Assert.Contains("<a href='/card?name=070-A-x' title='Banner'>070-A-x</a>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mute'>Result:</span> look at it", html, StringComparison.Ordinal);
    }

    // Requests to the planner: each waiting card's Depends on, and "all done" only where it is.
    [Fact]
    public void Planner_DependsAndAllDonePerCard()
    {
        var snapshot = ViewsFixture.Plain() with
        {
            Planner = new PagePlannerView
            {
                WaitingCards = ["060-E-a", "070-E-b"],
                WaitingDepends = new Dictionary<string, IReadOnlyList<string>> { ["060-E-a"] = ["010-A"], ["070-E-b"] = ["099-A"] },
                WaitingReady = ["060-E-a"],
            },
        };

        var html = WindowOf(Render(snapshot), "<b>Requests to the planner</b>");

        Assert.Contains("<th>Depends on</th>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='dep' data-dep='010-A'>010-A</span></td><td><span class='warn'>all done: the planner can act now</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='dep' data-dep='099-A'>099-A</span></td><td><span class='mute'>waiting</span>", html, StringComparison.Ordinal);
    }

    // The servers that do not answer, by name.
    [Fact]
    public void Control_DownServers_Named()
    {
        var html = Render(ViewsFixture.Plain() with { Health = new PageHealthView { ServerDown = true, DownServers = ["http://gpu:8080"] } });

        Assert.Contains("model server http://gpu:8080 does not answer", html, StringComparison.Ordinal);
    }

    // The page script lights up a depended-on card and keeps it lit across a refresh.
    [Fact]
    public void Script_DependsHighlight()
    {
        Assert.Contains("function applyHl()", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("closest('.dep')", PageScript.Html, StringComparison.Ordinal);
        Assert.Matches(@"pinLogScroll\(\);\s+applyHl\(\);", PageScript.Html);
    }

    // A quiet worker: the time says so and the spinner is stopped; the script does the same live.
    [Fact]
    public void Worker_Quiet_TimeAndStoppedSpinner()
    {
        var html = Render(ViewsFixture.Plain() with { Slots = [Running() with { QuietMinutes = 7 }] });

        Assert.Contains("<span class='tag' data-slot='1'>30/60 min, quiet 7 min</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mini idle' data-slot='1'><span class='spin'></span>", html, StringComparison.Ordinal);
        // 1092-C: the spinner follows the time this page holds for the slot, not the last update's, so
        // an answer that carried no time cannot restart a quiet worker's spinner.
        Assert.Contains("mi.classList.toggle('idle', /quiet/.test(k.time))", PageScript.Html, StringComparison.Ordinal);

        var busy = Render(ViewsFixture.Plain() with { Slots = [Running()] });
        Assert.Contains("<span class='mini' data-slot='1'><span class='spin'></span>", busy, StringComparison.Ordinal);
    }

    // A message not read yet: the old page's note beside Send.
    [Fact]
    public void Worker_MessagePending_Note()
    {
        const string Note = "<span class='warn'>waiting for the worker to read it</span>";
        Assert.Contains(Note, Render(ViewsFixture.Plain() with { Slots = [Running() with { MessagePending = true }] }), StringComparison.Ordinal);
        Assert.DoesNotContain(Note, Render(ViewsFixture.Plain() with { Slots = [Running()] }), StringComparison.Ordinal);
    }

    // 700-B: when the hook last handed the worker a message, beside the message state; nothing when
    // it has had none.
    [Fact]
    public void Worker_MessageReadAt_ReadTimeShown()
    {
        var read = new DateTimeOffset(2026, 11, 19, 14, 5, 9, TimeSpan.FromHours(-8));
        var html = Render(ViewsFixture.Plain() with { Slots = [Running() with { MessagePending = true, MessageReadAt = read }] });

        Assert.Contains("<span class='mute'>read at 14:05:09</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='warn'>waiting for the worker to read it</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span class='mute'>read at", Render(ViewsFixture.Plain() with { Slots = [Running()] }), StringComparison.Ordinal);
    }

    // WT-P1: an idle folder has no window at all, so there is nothing on the page to label idle or
    // disabled; a model server that does not answer is said once, in the Control window (510-B).
    [Fact]
    public void Worker_IdleFolder_HasNoWindow()
    {
        var html = Render(ViewsFixture.Plain() with { Slots = [new PageSlotView { Slot = 1, ServerDown = true }, new PageSlotView { Slot = 2 }] });

        Assert.DoesNotContain("href='/log?slot=1'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No card.", html, StringComparison.Ordinal);
        Assert.Contains("No workers running (0 of 2)", html, StringComparison.Ordinal);

        var down = Render(ViewsFixture.Plain() with { Health = new PageHealthView { DownServers = ["http://claude.local:8080/v1"] } });
        Assert.Contains("model server http://claude.local:8080/v1 does not answer", down, StringComparison.Ordinal);
    }

    // The health links its log - from the Control window's title since 610-C, where the health moved
    // to sit beside the state.
    [Fact]
    public void Control_HealthLog_Link()
    {
        Assert.Contains("<span class='mute'>main green</span> &nbsp; <a href='/health'>log</a>", Render(ViewsFixture.Plain() with { Health = new PageHealthView { Text = "main green", HasLog = true } }), StringComparison.Ordinal);
        Assert.DoesNotContain("<a href='/health'>log</a>", Render(ViewsFixture.Plain() with { Health = new PageHealthView { Text = "main green" } }), StringComparison.Ordinal);
    }
}
