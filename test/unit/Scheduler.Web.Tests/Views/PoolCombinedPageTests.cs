using System.Text.RegularExpressions;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// MP-8 (spec B1) with the page settings B3 Q12 settled on cards 330-B and 340-C: the pool's
/// config carries its own theme, port, listen and remote control, so the combined page wears the
/// theme it is handed and shows every project in one look - a project filter on each view, one
/// Waiting on you across projects, and stats per project and per model.
/// </summary>
public sealed class PoolCombinedPageTests
{
    private const string Theme = "slate";

    private static readonly DateTimeOffset Built = new(2026, 11, 18, 12, 30, 0, TimeSpan.Zero);

    // MP-8 · weighted-effort.md Pool_CombinedPage_FilterAndOneWaitingOnYou · Q12 (theme is top-level)
    [Fact]
    public void Pool_CombinedPage_FilterAndOneWaitingOnYou()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        // The pool's own theme, not a project's (Q12 as answered on 330-B Q3).
        Assert.Contains("<body data-pool-theme='slate'>", html, StringComparison.Ordinal);

        // One Waiting on you list for the whole pool, not one per project. The window is what counts:
        // the words are also in the worker windows' Stop and Kill tips (WT-P2's answer to Q4).
        Assert.Equal(1, Occurrences(html, "<b>Waiting on you</b>"));

        // Both projects' items are in it, each row labelled with the project it came from.
        Assert.Contains("title='AmigaX'>AmigaX</span>", html, StringComparison.Ordinal);
        Assert.Contains("title='Other'>Other</span>", html, StringComparison.Ordinal);
        foreach (var card in new[] { "120-A-dispatch.md", "090-C-page-views.md", "020-B-git.md", "080-B-config.md" })
        {
            Assert.Contains(card, html, StringComparison.Ordinal);
        }

        // AmigaX's question keeps its actions, and Other's review keeps its own note.
        Assert.Contains("which window?", html, StringComparison.Ordinal);
        Assert.Contains("the git retry needs a look", html, StringComparison.Ordinal);

        // Every row says which project it belongs to, so a filter can hide the other project's rows.
        Assert.Contains("<tr data-project='AmigaX'>", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-project='Other'>", html, StringComparison.Ordinal);

        // MP-P2: one project dropdown for the whole page, not one per view, holding All and every project.
        foreach (var view in new[] { "waiting", "statistics", "workers", "queue", "events" })
        {
            Assert.DoesNotContain($"<select class='projfilter' data-view='{view}'", html, StringComparison.Ordinal);
        }

        Assert.Equal(1, Occurrences(html, "<select id='projsel'"));
        Assert.Contains("<option value='all'>All projects</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value='AmigaX'>AmigaX</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value='Other'>Other</option>", html, StringComparison.Ordinal);

        // Stats per project and per model (ACCT-6 puts the model lines in the stats text).
        Assert.Contains("Last 24 h (5 h 5 min): 4 cards (5 runs)", html, StringComparison.Ordinal);
        Assert.Contains("Last 24 h (1 h 20 min): 2 cards (2 runs)", html, StringComparison.Ordinal);
        Assert.Contains("opus (3 h 5 min): 2 cards (3 runs)", html, StringComparison.Ordinal);
        Assert.Contains("gpt (2 h 0 min): 2 cards (2 runs)", html, StringComparison.Ordinal);
        Assert.Contains("sonnet (1 h 20 min): 2 cards (2 runs)", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats projonly' data-project='AmigaX' style='white-space: pre-wrap'>", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats projonly' data-project='Other' style='white-space: pre-wrap'>", html, StringComparison.Ordinal);
    }

    // 520-A (the developer, 2026-11-18): the same on the combined page - the overall line and each
    // model's line on a line of their own, each model line keeping its indent.
    [Fact]
    public void Pool_Stats_EachLineOnItsOwnLine_KeepingTheIndent()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        var lines = Regex.Matches(html, "<div class='stats projonly' data-project='AmigaX'([^>]*)>(.*?)</div>")
            .Select(m => (Attrs: m.Groups[1].Value, Text: m.Groups[2].Value))
            .ToList();
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("Last 24 h (5 h 5 min): 4 cards (5 runs)", lines[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("  opus (3 h 5 min): 2 cards (3 runs)", lines[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("  gpt (2 h 0 min): 2 cards (2 runs)", lines[2].Text, StringComparison.Ordinal);
        foreach (var line in lines)
        {
            Assert.Contains("white-space: pre-wrap", line.Attrs, StringComparison.Ordinal);
            Assert.DoesNotContain("%", line.Text, StringComparison.Ordinal);
        }
    }

    // 520-A: the combined page has no top bar either; the pool line, the selector and the newest
    // tick sit in the first window's title bar. 1004-C: the line is the single page's - name and
    // version, state, health, "N of M workers" - then the pool's own part.
    [Fact]
    public void Pool_HasNoTopBar_ThePoolLineIsInTheFirstWindowsBar()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        Assert.DoesNotContain("id='screen'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class='screen'", html, StringComparison.Ordinal);
        Assert.Contains("<div class='desk' id='desk'>", html, StringComparison.Ordinal);
        // 1004-C (the developer, 2026-11-20 18:51): no state, health or workers summary after the version.
        // 1103-C: the CPU and memory text sits in its own element, which the page script moves with the
        // stream's frames; it is there empty when the snapshot has no load sample to show.
        Assert.Contains(
            "<b>Switchyard v" + System.Net.WebUtility.HtmlEncode(ViewsFixture.ShownVersion) + " <span id='hostload'></span> <span id='live-state'></span> &nbsp; theme slate</b>",
            html,
            StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<select id='themesel'"));
        Assert.Contains("last update " + Built.ToString("HH:mm:ss"), html, StringComparison.Ordinal);
    }

    // MP-8: the same page read from another PC has its controls disabled, as the single page does (PAGE-7).
    [Fact]
    public void Pool_CombinedPage_ReadOnlyForRemoteReader()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: true, controlsAllowed: false);

        Assert.Contains("disabled", html, StringComparison.Ordinal);
        Assert.Contains("(Only on the PC running the scheduler.)", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<b>Waiting on you</b>"));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // 971-C (the developer, 2026-11-19): the model server's address is not shown in a worker window
    // on this page either; the type and the model stay, and the address lives in the Worker types
    // panel, where it is set.
    [Fact]
    public void Pool_WorkerWindow_ShowsNoModelServerAddress()
    {
        const string endpoint = "http://spark.local:18400/v1";
        var amiga = AmigaX();
        var slots = amiga.Slots
            .Select(slot => slot with { Stats = $"context ~2k  |  7 model calls  |  21 tools  |  {slot.Model}  |  {endpoint}" })
            .ToList();

        var html = new CombinedPageRenderer().Render([amiga with { Slots = slots }, Other()], Theme, remote: false, controlsAllowed: true);

        Assert.DoesNotContain(endpoint, html, StringComparison.Ordinal);
        Assert.Contains(
            "<div class='stats' data-slot='AmigaX:1' data-model='claude-opus-4'>context ~2k  |  7 model calls  |  21 tools</div>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("opus (claude-opus-4) - <a href='/card?project=AmigaX&name=120-A-dispatch.md'>", html, StringComparison.Ordinal);
    }

    // MP-P2: one dropdown in the bar, beside the theme menu, for the whole page; the choice is kept in
    // the browser and filters every window by class, so the pending mask's own hiding survives it.
    [Fact]
    public void MP_P2_OneDropdownBesideTheTheme_ForTheWholePage()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        var bar = html.IndexOf("<select id='projsel'", StringComparison.Ordinal);
        Assert.True(bar >= 0 && bar < html.IndexOf("<select id='themesel'", StringComparison.Ordinal), "the dropdown sits before the theme menu");
        Assert.Equal(1, Occurrences(html, "<select id='projsel'"));
        Assert.DoesNotContain("projfilter", html, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(projKey", html, StringComparison.Ordinal);
        Assert.Contains("rows[i].classList.toggle('projhide', v !== 'all' && mine !== v);", html, StringComparison.Ordinal);
        Assert.Contains("if (window.applyFilters) window.applyFilters();", html, StringComparison.Ordinal);
    }

    // MP-P2, MP-P9: with one project there is no dropdown and no chip; the page looks as today.
    [Fact]
    public void MP_P2_OneProject_NoDropdownNoChip()
    {
        var html = new CombinedPageRenderer().Render([AmigaX()], Theme, remote: false, controlsAllowed: true);

        Assert.DoesNotContain("<select id='projsel'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span class='chip projchip'", html, StringComparison.Ordinal);
        Assert.Contains("<span class='proj'>AmigaX</span>", html, StringComparison.Ordinal);
        // 1004-C (18:59 item 14): the one exception is a worker window, which always shows its project's chip.
        Assert.Equal(1, Occurrences(html, "<span class='chip projchip wchip'"));
    }

    // 1004-C (18:58 item 15): the two columns' first windows start at the same top edge. Queue is the
    // first thing in the side column, and the side column is not held 60 px down by the layout's
    // sticky offset, which is meant for a page with a bar above its desk.
    [Fact]
    public void Card1004_ColumnTops_LineUp()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        Assert.Contains("<div class='col' id='col-side'><div class='col' id='col-queue'><section class='win' id='w-queue'", html, StringComparison.Ordinal);
        Assert.Contains("<div class='col' id='col-main'><section class='win' id='w-projects'", html, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 1400px) { #col-side { top: 0; max-height: 100vh; } }", html, StringComparison.Ordinal);
    }

    // MP-P9: under All, every project row carries a chip in the category chip's style - a colour of its
    // own, stable whatever the pool file's order, white text readable at 4.5:1, none of the category
    // colours - with the full name on hover; the page hides the chips while one project is picked.
    [Fact]
    public void MP_P9_ProjectChip_OnEveryRow_StableColour_HiddenUnderOneProject()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        // The Projects window, Waiting on you, Statistics, Queue and Recent events: two of each at least.
        Assert.True(Occurrences(html, "<span class='chip projchip'") >= 10);
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) .projchip { display: none; }", html, StringComparison.Ordinal);

        var amiga = ProjectChip.Colour("AmigaX", ["AmigaX", "Other"]);
        Assert.Equal(amiga, ProjectChip.Colour("AmigaX", ["Other", "AmigaX"]));
        Assert.NotEqual(amiga, ProjectChip.Colour("Other", ["AmigaX", "Other"]));
        Assert.Contains($"style='background:{amiga};color:#ffffff' title='AmigaX'>AmigaX</span>", html, StringComparison.Ordinal);
        foreach (var colour in ProjectChip.Palette)
        {
            Assert.DoesNotContain(colour, CategoryChip.Palette);
            Assert.True(CategoryChip.ContrastWithWhite(colour) >= 4.5, colour);
        }

        Assert.Contains(
            "title='A project with a long name'>A project w…</span>",
            ProjectChip.Html("A project with a long name", ["A project with a long name", "x"]),
            StringComparison.Ordinal);
    }

    // 1105-C (the developer, 2026-11-22): VW-4's chip hiding must not take the Projects table's name
    // away with it. There the chip is the row's name, so the table keeps it while the dropdown is on
    // one project; the notice, the queue rows and the events keep VW-4's rule as before.
    [Fact]
    public void VW_4_ProjectsTable_KeepsProjectName_WhileOneProjectIsChosen()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        // VW-4's rule, then the Projects table's exemption from it - and both of them inside the desk
        // they decide, not in the head the live feed never replaces (1105-C, the developer 17:42).
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) .projchip { display: none; }", html, StringComparison.Ordinal);
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) table.projects .projchip { display: inline-block; }", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "table.projects .projchip"));

        // Every row of the Projects table carries its chip - the project's name, in its own colour.
        var from = html.IndexOf("<table class='projects'>", StringComparison.Ordinal);
        var table = html[from..html.IndexOf("</table>", from, StringComparison.Ordinal)];
        foreach (var project in new[] { "AmigaX", "Other" })
        {
            var colour = ProjectChip.Colour(project, ["AmigaX", "Other"]);
            Assert.Contains(
                $"<td class='nw'><span class='chip projchip' style='background:{colour};color:#ffffff' title='{project}'>{project}</span></td>",
                table,
                StringComparison.Ordinal);
        }

        // Elsewhere nothing was exempted: only the general rule covers the chips of the other tables,
        // so they still go with one project picked.
        var wfrom = html.IndexOf("<table class='waiting'>", StringComparison.Ordinal);
        var waiting = html[wfrom..html.IndexOf("</table>", wfrom, StringComparison.Ordinal)];
        Assert.Contains("<span class='chip projchip'", waiting, StringComparison.Ordinal);
        Assert.DoesNotContain("table.waiting .projchip", html, StringComparison.Ordinal);
        Assert.DoesNotContain("table.events .projchip", html, StringComparison.Ordinal);
    }

    // 1105-C (the developer, 2026-11-22 17:42: "no, still empty"). The exemption was in the page, but
    // in its head, and the live feed replaces #desk whole and never the head (1098-C). A page opened
    // before the deploy therefore kept MP-P9's hiding rule without the rule exempting the Projects
    // table, so its PROJECT cell stayed empty however often the desk came back. The chip rules now
    // come with the desk they decide, so the head a page happens to have cannot strand a name.
    [Fact]
    public void VW_4_ProjectChipRules_RideInsideTheDesk_WithTheMarkupTheyGovern()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        var head = html[..html.IndexOf("<body", StringComparison.Ordinal)];
        var from = html.IndexOf("<div class='desk' id='desk'>", StringComparison.Ordinal);
        Assert.True(from >= 0);
        var desk = html[from..];

        // One copy governs, and it is the desk's: the head keeps no chip rule of its own.
        Assert.DoesNotContain(".projchip", head, StringComparison.Ordinal);
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) .projchip { display: none; }", desk, StringComparison.Ordinal);
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) table.projects .projchip { display: inline-block; }", desk, StringComparison.Ordinal);

        // It also precedes the table it decides, and follows the range rules it rides beside.
        var rules = desk.IndexOf("<style data-projchip-rules>", StringComparison.Ordinal);
        Assert.True(rules >= 0);
        Assert.True(rules > desk.IndexOf("<style data-range-rules>", StringComparison.Ordinal));
        Assert.True(rules < desk.IndexOf("<table class='projects'>", StringComparison.Ordinal));
    }

    // MP-P3: one Waiting on you; each card row carries its project and card for the pending mask, and
    // every button posts project= beside card= (question choices with q= and a= too).
    [Fact]
    public void MP_P3_WaitingRowsLabelled_ButtonsPostProject()
    {
        var question = new CardQuestion
        {
            Number = 1,
            Text = "Which window?",
            Choices = [new CardChoice { Label = "left" }, new CardChoice { Label = "right" }],
        };
        var amiga = AmigaX() with
        {
            Waiting = [new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "120-A-dispatch.md", Note = "which window?", Questions = [question] }],
        };
        var html = new CombinedPageRenderer().Render([amiga, Other()], Theme, remote: false, controlsAllowed: true);

        Assert.Contains("<tr data-project='AmigaX' data-card='120-A'>", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-project='Other' data-card='080-B'>", html, StringComparison.Ordinal);
        Assert.Contains("<tr class='qrow' data-project='AmigaX'>", html, StringComparison.Ordinal);
        // 1099-C: each card has one note form carrying project and card; its choice and Done buttons reach it by id.
        Assert.Contains(
            "<form id='n-AmigaX-120-A' method='post' action='/note' class='g'",
            html,
            StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='AmigaX'><input type='hidden' name='card' value='120-A-dispatch.md'>", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-AmigaX-120-A' formaction='/answer?q=1' name='a' value='1' title='right'>right</button>", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-Other-080-B' formaction='/done'", html, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='Other'><input type='hidden' name='card' value='080-B-config.md'>", html, StringComparison.Ordinal);
    }

    // 1010-C: Open on Waiting rows and Recent events rows carries the project; question rows have the
    // answer note box; the worker windows have the message box posting to /send.
    [Fact]
    public void Card1010_PoolPage_OpenButtons_AnswerNote_WorkerMessageBox()
    {
        var question = new CardQuestion
        {
            Number = 1,
            Text = "Which window?",
            Choices = [new CardChoice { Label = "left" }, new CardChoice { Label = "right" }],
        };
        var amiga = AmigaX() with
        {
            Waiting = [new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "120-A-dispatch.md", Note = "which window?", Questions = [question] }],
            Events = [new PageEvent { Time = Built, Text = "t", Tone = PageTone.Ok, Card = "100-A-first.md", Result = "done" }],
        };
        var html = new CombinedPageRenderer().Render([amiga, Other()], Theme, remote: false, controlsAllowed: true);
        var waiting = Regex.Match(html, "id='w-waiting'.*?</section>", RegexOptions.Singleline).Value;
        var events = Regex.Match(html, "id='w-events'.*?</section>", RegexOptions.Singleline).Value;

        Assert.Contains("action='/open' class='g'", waiting, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='AmigaX'><input type='hidden' name='card' value='120-A-dispatch.md'>", waiting, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='Other'><input type='hidden' name='card' value='080-B-config.md'>", waiting, StringComparison.Ordinal);
        Assert.Contains("action='/open' class='g'", events, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='AmigaX'><input type='hidden' name='card' value='100-A-first.md'>", events, StringComparison.Ordinal);
        Assert.Contains("<input name='note' maxlength='500' placeholder='note (optional)'>", waiting, StringComparison.Ordinal);
        Assert.Contains("placeholder='Message to this worker'", html, StringComparison.Ordinal);
        Assert.Contains("action='/send", html, StringComparison.Ordinal);
    }

    // 1004-C (replaces MP-P4's weight and share columns, as the developer's picture of the page has
    // it): the Projects table is PROJECT, HEALTH, then each row's own buttons in their own columns, in
    // the order Check main, Categories, Planner push, Pause or Resume, Drain, Stop. Stop is the
    // project's own (/stop-project), never Stop all and exit; Pause is one button that says Resume
    // while the project is paused.
    [Fact]
    public void Card1004_ProjectsTable_RowButtonsInOrder_PauseToggles_StopIsTheProjects()
    {
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Running = 2,
            Projects =
            [
                new PoolProjectView { Name = "AmigaX", Weight = 70, WeightShare = 0.7, UsedShare = 0.55, Running = 1, Queued = 4 },
                new PoolProjectView { Name = "Other", Weight = 30, WeightShare = 0.3, UsedShare = 0.45, Running = 1, Queued = 2 },
            ],
            Pages = [AmigaX() with { PlannerPush = true }, Other()],
        };
        var html = new CombinedPageRenderer(theme: Theme).Render(pool, remote: false, controlsAllowed: true);

        Assert.Contains("<body data-pool-theme='slate'>", html, StringComparison.Ordinal);
        Assert.Contains("<table class='projects'><tr><th>Project</th><th>Health</th><th colspan='6'></th></tr>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Weight</th>", html, StringComparison.Ordinal);

        var amiga = Row(html, "AmigaX");
        AssertInOrder(amiga, "main 1a2b3c4 green at 12:20", "action='/health-now'", "action='/open-lanes'", "action='/planner-toggle'", "action='/pause'", "action='/drain'", "action='/stop-project'");
        Assert.Equal(8, Regex.Matches(amiga, "<td ").Count);
        Assert.Contains(">Pause</button>", amiga, StringComparison.Ordinal);
        Assert.DoesNotContain("action='/resume'", amiga, StringComparison.Ordinal);
        Assert.Contains("<button class='pressed ok' aria-label='Planner push: on.", amiga, StringComparison.Ordinal);
        Assert.Contains(">Categories</button>", amiga, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='AmigaX'><button class='bad' aria-label='Stop. ", amiga, StringComparison.Ordinal);

        // Other is paused: its one toggle reads Resume; it has no planner, so that place is empty and the columns still line up.
        var other = Row(html, "Other");
        AssertInOrder(other, "action='/health-now'", "action='/open-lanes'", "<td class='nw rowbtn'></td>", "action='/resume'", "action='/drain'", "action='/stop-project'");
        Assert.Equal(8, Regex.Matches(other, "<td ").Count);
        Assert.Contains(">Resume</button>", other, StringComparison.Ordinal);
        Assert.DoesNotContain("action='/pause'", other, StringComparison.Ordinal);

        foreach (var row in new[] { amiga, other })
        {
            Assert.DoesNotContain("Stop all and exit", row, StringComparison.Ordinal);
            Assert.DoesNotContain("action='/stopall'", row, StringComparison.Ordinal);
        }
    }

    // 1004-C: the title bar's buttons act on every project - Pause all (Resume all once none runs),
    // Drain and exit, Stop all and exit, left to right - and post no project; then the dropdown.
    [Fact]
    public void Card1004_TitleBar_PauseAllDrainStopAll_InOrder_PauseAllToggles()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);
        var bar = Regex.Match(html, "id='w-projects'[^>]*><div class='bar'>(.*?)</div><div class='inner'>", RegexOptions.Singleline).Groups[1].Value;

        AssertInOrder(bar, ">Pause all</button>", ">Drain and exit</button>", ">Stop all and exit</button>", "<select id='projsel'", "<select id='themesel'", "last update");
        Assert.Contains("<form method='post' action='/pause' class='g' data-tip='Start no new workers in any project; running ones finish.'><button", bar, StringComparison.Ordinal);
        Assert.Contains("<form method='post' action='/drain' class='g' data-tip='Every project starts nothing new; running cards finish, then the scheduler exits.'><button class='bad'", bar, StringComparison.Ordinal);
        Assert.Contains("action='/stopall'", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("Resume all", bar, StringComparison.Ordinal);

        var paused = new CombinedPageRenderer().Render([AmigaX() with { State = Other().State }, Other()], Theme, remote: false, controlsAllowed: true);
        Assert.Contains("<form method='post' action='/resume' class='g' data-tip='No project is taking cards: click to start workers in all of them again.'><button class='pressed ok' aria-label='Resume all.", paused, StringComparison.Ordinal);
        Assert.DoesNotContain(">Pause all</button>", paused, StringComparison.Ordinal);
    }

    // 1004-C: one window holds the table, the statistics and the Worker types; the old Workers and
    // Statistics windows and the pool page's Categories panel are gone, and the running workers'
    // windows stand on their own after it.
    [Fact]
    public void Card1004_OneWindow_TableStatisticsWorkerTypes_OldWindowsGone()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);
        var window = Regex.Match(html, "<section class='win' id='w-projects'.*?</section>", RegexOptions.Singleline).Value;

        AssertInOrder(window, "<table class='projects'>", "<div class='poolstats'>", "Last 24 h (", "class='wttoggle'");
        Assert.Equal(1, Occurrences(html, "<section class='win' id='w-projects'"));
        Assert.Equal(1, Occurrences(html, "class='wttoggle'"));
        foreach (var gone in new[] { "id='w-statistics'", "id='w-workers'", "<b>Workers</b>", "<b>Statistics</b>", "wtypes wthidden cats", "class='poolctl'", "class='plannerrow'" })
        {
            Assert.DoesNotContain(gone, html, StringComparison.Ordinal);
        }

        Assert.Contains("</section><section class='win' data-project='AmigaX' data-card='120-A'>", html, StringComparison.Ordinal);
    }

    // 1004-C: the dropdown filters the rows and the statistics - under All projects only the lines
    // summed over every project show, with one project picked only that project's own.
    [Fact]
    public void Card1004_Dropdown_FiltersRowsAndStatistics()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        Assert.Contains("<tr data-project='AmigaX'><td class='nw'><span class='chip projchip'", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-project='Other'><td class='nw'><span class='chip projchip'", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats projonly' data-project='AmigaX' style='white-space: pre-wrap'>Last 24 h (5 h 5 min)", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats projonly' data-project='Other' style='white-space: pre-wrap'>Last 24 h (1 h 20 min)", html, StringComparison.Ordinal);
        Assert.Contains(
            "<div class='stats allonly' style='white-space: pre-wrap'>Last 24 h (6 h 25 min): 6 cards (7 runs): done 4, 2 in review, 0 waiting on questions, 0 open; 0.25 cards/hr</div>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("body:not([data-projsel]) .projonly, body[data-projsel='all'] .projonly, body[data-projsel]:not([data-projsel='all']) .allonly { display: none; }", html, StringComparison.Ordinal);
        Assert.Contains("rows[i].classList.toggle('projhide', v !== 'all' && mine !== v);", html, StringComparison.Ordinal);

        // A pool of one: its own lines, no summed set, nothing hidden by the All rule.
        var one = new CombinedPageRenderer().Render([AmigaX()], Theme, remote: false, controlsAllowed: true);
        Assert.Contains("<div class='stats' data-project='AmigaX' style='white-space: pre-wrap'>Last 24 h (5 h 5 min)", one, StringComparison.Ordinal);
        Assert.DoesNotContain("class='stats allonly'", one, StringComparison.Ordinal);
    }

    // 1004-C Dev note 1: the Queue window has no header row.
    [Fact]
    public void Card1004_Queue_NoHeaderRow()
    {
        var amiga = AmigaX() with { Queue = [new PageQueueItem { Name = "130-A-next.md", Title = "Next" }] };
        var html = new CombinedPageRenderer().Render([amiga, Other()], Theme, remote: false, controlsAllowed: true);
        var queue = Regex.Match(html, "id='w-queue'.*?</section>", RegexOptions.Singleline).Value;

        Assert.Contains("130-A-next.md", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>", queue, StringComparison.Ordinal);
    }

    // 1004-C Dev notes 2-4: a Recent events run row has no worker or slot column and no run time; its
    // card links to the card's file view, as on the one-project page; a line that is no run keeps its text.
    [Fact]
    public void Card1004_RecentEvents_NoWorkerSlotOrTime_CardLinksToItsView()
    {
        var amiga = AmigaX() with
        {
            Events =
            [
                new PageEvent
                {
                    Time = Built.AddMinutes(-3), Text = "slot 1  100-A-first.md: done, 12 min", Tone = PageTone.Ok,
                    Type = "opus", Slot = 1, Model = "claude-opus-4", Card = "100-A-first.md", Result = "done", Minutes = 12,
                },
                new PageEvent { Time = Built.AddMinutes(-4), Text = "main is green again", Tone = PageTone.Ok },
            ],
        };
        var html = new CombinedPageRenderer().Render([amiga, Other()], Theme, remote: false, controlsAllowed: true);
        var events = Regex.Match(html, "id='w-events'.*?</section>", RegexOptions.Singleline).Value;
        var run = Regex.Match(events, "<tr data-project='AmigaX'>(?:(?!</tr>).)*100-A-first(?:(?!</tr>).)*</tr>", RegexOptions.Singleline).Value;

        Assert.Contains("<td class='card'><a href='/card?project=AmigaX&name=100-A-first.md' title='AmigaX - 100-A-first.md'>100-A-first.md</a></td>", run, StringComparison.Ordinal);
        Assert.Contains("<span class='ok'>done</span>", run, StringComparison.Ordinal);
        Assert.DoesNotContain("slot", run, StringComparison.Ordinal);
        Assert.DoesNotContain("opus", run, StringComparison.Ordinal);
        Assert.DoesNotContain("12 min", run, StringComparison.Ordinal);
        Assert.Equal(6, Regex.Matches(run, "<td ").Count);
        Assert.Contains("main is green again</td>", events, StringComparison.Ordinal);
    }

    // 1004-C Dev note (Trends): under All projects one chart of every project's days summed per day
    // and model; with one project picked, that project's own chart. A pool of one keeps its own.
    [Fact]
    public void Card1004_Trends_AllProjectsSummed_OneProjectItsOwn()
    {
        var d1 = new DateOnly(2026, 11, 18);
        var d2 = new DateOnly(2026, 11, 19);
        static TrendSegment Seg(string model, int cards, int runs) => new() { Model = model, Label = model, CardsDone = cards, Runs = runs };
        var amiga = AmigaX() with
        {
            Trends = [new TrendDay { Date = d1, Segments = [Seg("opus", 2, 3)] }, new TrendDay { Date = d2, Segments = [Seg("opus", 1, 1), Seg("gpt", 1, 2)] }],
        };
        var other = Other() with
        {
            Trends = [new TrendDay { Date = d1, Segments = [Seg("opus", 3, 4)] }, new TrendDay { Date = d2, Segments = [Seg("sonnet", 4, 4)] }],
        };

        var summed = CombinedPageRenderer.SummedTrends([amiga, other]);
        Assert.Equal([d1, d2], summed.Select(day => day.Date));
        Assert.Equal((5, 7), (summed[0].CardsDone, summed[0].Runs));
        Assert.Equal(["opus 1", "gpt 1", "sonnet 4"], summed[1].Segments.Select(s => s.Model + " " + s.CardsDone));

        var html = new CombinedPageRenderer().Render([amiga, other], Theme, remote: false, controlsAllowed: true);
        var trends = Regex.Match(html, "id='w-trends'.*?</section>", RegexOptions.Singleline).Value;
        Assert.Equal(1, Occurrences(trends, "<div class='trendproj allonly'><div class='trendname'>All projects</div>"));
        Assert.Contains("<div class='trendproj projonly' data-project='AmigaX'>", trends, StringComparison.Ordinal);
        Assert.Contains("<div class='trendproj projonly' data-project='Other'>", trends, StringComparison.Ordinal);
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) .allonly { display: none; }", html, StringComparison.Ordinal);

        var one = new CombinedPageRenderer().Render([amiga], Theme, remote: false, controlsAllowed: true);
        Assert.Contains("<div class='trendproj' data-project='AmigaX'>", one, StringComparison.Ordinal);
        Assert.DoesNotContain("allonly", Regex.Match(one, "id='w-trends'.*?</section>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
    }

    // 1004-C (18:59 item 14): the chip a worker window leads with, shown whatever the dropdown picks.
    private static string WorkerChip(string project) =>
        ProjectChip.Html(project, ["AmigaX", "Other"]).Replace("class='chip projchip'", "class='chip projchip wchip'", StringComparison.Ordinal);

    // 1029-B: each project's row shows how many of its workspaces are busy, amber when all are; the header names the column.
    [Fact]
    public void Card1029_ProjectsRow_ShowsBusyWorkspaces()
    {
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Running = 5,
            Projects =
            [
                new PoolProjectView { Name = "AmigaX", Weight = 70, Running = 3, Workspaces = 4 },
                new PoolProjectView { Name = "Other", Weight = 30, Running = 2, Workspaces = 2 },
            ],
            Pages = [AmigaX(), Other()],
        };
        var html = new CombinedPageRenderer(theme: Theme).Render(pool, remote: false, controlsAllowed: true);

        Assert.Contains("<tr><th>Project</th><th>Health</th><th>Workspaces</th><th colspan='6'></th></tr>", html, StringComparison.Ordinal);
        Assert.Matches("<td class='nw'><form[^>]*><input type='hidden' name='project' value='AmigaX'><input type='number' name='workspaces'[^>]*value='4'[^>]*></form> <span class='mute'[^>]*>3 busy</span></td>", Row(html, "AmigaX"));
        Assert.Matches("value='2'[^>]*></form> <span class='warn'[^>]*>2 busy</span></td>", Row(html, "Other"));
    }

    // 1033-C: without control (remote viewer) the workspaces are the number and the busy count, with no box or form.
    [Fact]
    public void Card1033_WorkspacesColumn_ReadOnlyWithoutControl()
    {
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 70, Running = 3, Workspaces = 4 }, new PoolProjectView { Name = "Other", Weight = 30, Workspaces = 2 }],
            Pages = [AmigaX(), Other()],
        };
        var html = new CombinedPageRenderer(theme: Theme).Render(pool, remote: true, controlsAllowed: false);

        var row = Row(html, "AmigaX");
        Assert.DoesNotContain("name='workspaces'", row, StringComparison.Ordinal);
        Assert.Matches("<td class='nw'><span title='[^']*'>4</span> <span class='mute'[^>]*>3 busy</span></td>", row);
    }

    // 1033-C: the box posts to /workspaces for its own project, 1..64, and the page script posts it on change.
    [Fact]
    public void Card1033_WorkspacesColumn_BoxPostsForItsProject()
    {
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 70, Running = 3, Workspaces = 4 }, new PoolProjectView { Name = "Other", Weight = 30, Workspaces = 2 }],
            Pages = [AmigaX(), Other()],
        };
        var html = new CombinedPageRenderer(theme: Theme).Render(pool, remote: false, controlsAllowed: true);

        var row = Row(html, "Other");
        Assert.Contains("action='/workspaces'", row, StringComparison.Ordinal);
        Assert.Contains("name='project' value='Other'", row, StringComparison.Ordinal);
        Assert.Contains("min='1' max='64'", row, StringComparison.Ordinal);
        Assert.Contains("data-stored='2'", row, StringComparison.Ordinal);
        Assert.Contains("addEventListener('change'", html, StringComparison.Ordinal);
    }

    // 1055-C: the Projects table lists the projects by name, A-Z without case, a joining one among them; the
    // pool file's order (and so the chips' colours) is not the rows' order.
    [Fact]
    public void Card1055_ProjectsRows_SortedByName()
    {
        var pages = new[] { Other() with { Project = "Zulu" }, AmigaX(), Other() with { Project = "beta" } };
        var renderer = new CombinedPageRenderer(() => PageThemes.BuiltIn, Theme, () => ["Mike"]);

        var html = renderer.Render(pages, Theme, remote: false, controlsAllowed: true);

        var table = Regex.Match(html, "<table class='projects'>.*?</table>", RegexOptions.Singleline).Value;
        var rows = Regex.Matches(table, "<tr data-project='([^']*)'").Select(m => m.Groups[1].Value);
        Assert.Equal(["AmigaX", "beta", "Mike", "Zulu"], rows);
    }

    // 1055-C: with All projects, the Queue goes in the pool's start order; a row the order does not name follows.
    [Fact]
    public void Card1055_Queue_FollowsThePoolOrder()
    {
        var amiga = AmigaX() with { Queue = [new PageQueueItem { Name = "1-A-one", Title = "One" }, new PageQueueItem { Name = "2-A-two", Title = "Two" }] };
        var other = Other() with { Queue = [new PageQueueItem { Name = "3-B-three", Title = "Three" }, new PageQueueItem { Name = "4-B-four", Title = "Four" }] };
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 75 }, new PoolProjectView { Name = "Other", Weight = 25 }],
            QueueOrder =
            [
                new Scheduler.Core.Pool.PoolQueueEntry("AmigaX", "1-A-one"),
                new Scheduler.Core.Pool.PoolQueueEntry("Other", "3-B-three"),
                new Scheduler.Core.Pool.PoolQueueEntry("AmigaX", "2-A-two"),
            ],
            Pages = [amiga, other],
        };

        var html = new CombinedPageRenderer(theme: Theme).Render(pool, remote: false, controlsAllowed: true);

        var queue = Regex.Match(html, "<table class='queue'>.*?</table>", RegexOptions.Singleline).Value;
        var cards = Regex.Matches(queue, "data-card='([^']*)'").Select(m => m.Groups[1].Value);
        Assert.Equal(["1-A", "3-B", "2-A", "4-B"], cards);
    }

    // A project's row of the Projects table, from its <tr> to the next.
    private static string Row(string html, string project) =>
        Regex.Match(html, "<tr data-project='" + project + "'>.*?</tr>", RegexOptions.Singleline).Value;

    private static void AssertInOrder(string text, params string[] parts)
    {
        var at = 0;
        foreach (var part in parts)
        {
            var next = text.IndexOf(part, at, StringComparison.Ordinal);
            Assert.True(next >= 0, $"'{part}' missing or out of order");
            at = next + part.Length;
        }
    }

    // MP-P9: the chip's hover reads "<name> - <repo>" when the project has a folder, the name alone otherwise.
    [Fact]
    public void MP_P9_ChipTitle_NameAndRepo()
    {
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Projects =
            [
                new PoolProjectView { Name = "AmigaX", Repo = @"C:\repos\Amiga & X" },
                new PoolProjectView { Name = "Other", Repo = "" },
            ],
            Pages = [AmigaX(), Other()],
        };
        var html = new CombinedPageRenderer(theme: Theme).Render(pool, remote: false, controlsAllowed: true);

        Assert.Contains("title='AmigaX - C:\\repos\\Amiga &amp; X'", html, StringComparison.Ordinal);
        Assert.Contains("title='Other'", html, StringComparison.Ordinal);
    }

    // MP-P6: a worker window's title names its project, then the type, slot and card; Stop and Kill
    // post project= and slot= as fields, and the log and stats are keyed project:slot (WT-W3).
    [Fact]
    public void MP_P6_WorkerWindowTitle_NamesProject()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        // 1004-C (18:57 item 13a): the project's chip, the type and model, and the card linked to its view; no slot number.
        Assert.Contains(
            "<b>" + WorkerChip("AmigaX") + " opus (claude-opus-4) - <a href='/card?project=AmigaX&name=120-A-dispatch.md'>120-A-dispatch.md</a></b>",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<b>" + WorkerChip("Other") + " sonnet (gpt-5) - <a href='/card?project=Other&name=020-B-git.md'>020-B-git.md</a></b>",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("slot 1", html, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='Other'><input type='hidden' name='slot' value='2'>", html, StringComparison.Ordinal);
        Assert.Contains("<pre class='log' data-slot='Other:2'>", html, StringComparison.Ordinal);
    }

    // MP-P5, 1004-C: under All projects the statistics are every project's runs summed, in the lines
    // the single page writes - the Last 24 h line, then each model summed across the projects that ran it.
    [Fact]
    public void MP_P5_AllProjectsLine_SumsEachModel()
    {
        var other = Other() with
        {
            Stats = Other().Stats with
            {
                Text = Other().Stats.Text + "\n  opus (0 h 55 min): 1 cards (2 runs): done 1, 0 in review, 0 waiting on questions, 0 open; 0.1 cards/hr",
            },
        };
        var html = new CombinedPageRenderer().Render([AmigaX(), other], Theme, remote: false, controlsAllowed: true);

        Assert.Contains("<div class='stats allonly' style='white-space: pre-wrap'>Last 24 h (6 h 25 min): 6 cards (7 runs): done 4, 2 in review, 0 waiting on questions, 0 open; 0.25 cards/hr</div>", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats allonly' style='white-space: pre-wrap'>  opus (4 h 0 min): 3 cards (5 runs): done 3, 0 in review", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats allonly' style='white-space: pre-wrap'>  gpt (2 h 0 min): 2 cards (2 runs): done 1, 1 in review", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats allonly' style='white-space: pre-wrap'>  sonnet (1 h 20 min): 2 cards (2 runs): done 1, 1 in review", html, StringComparison.Ordinal);
        Assert.DoesNotContain("All projects</b>", html, StringComparison.Ordinal);

        // Under an hour the time reads as CardStats writes it; no runs anywhere is its own line.
        var short1 = AmigaX() with { Stats = new PageStats { Text = "Last 24 h (20 min): 1 cards (1 runs): done 1, 0 in review, 0 waiting on questions, 0 open; 0.04 cards/hr" } };
        var short2 = Other() with { Stats = new PageStats { Text = "Last 24 h (15 min): 1 cards (1 runs): done 0, 0 in review, 0 waiting on questions, 1 open; 0 cards/hr" } };
        Assert.Equal(
            ["Last 24 h (35 min): 2 cards (2 runs): done 1, 0 in review, 0 waiting on questions, 1 open; 0.04 cards/hr"],
            CombinedPageRenderer.SummedStats([short1, short2]));
        var none = AmigaX() with { Stats = new PageStats { Text = "Last 24 h: no runs finished." } };
        Assert.Equal(["Last 24 h: no runs finished."], CombinedPageRenderer.SummedStats([none, none with { Project = "Other" }]));

        var one = new CombinedPageRenderer().Render([AmigaX()], Theme, remote: false, controlsAllowed: true);
        Assert.DoesNotContain("allonly'", one, StringComparison.Ordinal);
    }

    // MP-P7, 1004-C: the Worker types panel exactly as on the single page - collapsible, with its
    // one-line summary - once for the pool: the running counts summed over the projects, its Save
    // posting /worker-types with no project (the pool file, 1000-B), a Projects column; not filtered.
    [Fact]
    public void MP_P7_PoolWorkerTypes_OnePanelAsTheSinglePage()
    {
        PageWorkerTypeView[] types =
        [
            new() { Name = "opus", Max = 2, Agent = "claude", Projects = ["AmigaX"] },
            new() { Name = "sonnet", Max = 4 },
        ];
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Running = 2,
            Pages =
            [
                AmigaX() with { WorkerTypes = [types[0] with { Running = 1 }, types[1]] },
                Other() with { WorkerTypes = [types[0], types[1] with { Running = 1 }] },
            ],
        };
        var html = new CombinedPageRenderer().Render(pool, remote: false, controlsAllowed: true);

        Assert.Equal(1, Occurrences(html, "class='wttoggle'"));
        Assert.Contains("<form method='post' action='/worker-types' class='wtypes wthidden'><div class='wtypesbar'>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mute wtsum'>opus 1 of 2, sonnet 1 of 4 - max 6 active</span>", html, StringComparison.Ordinal);
        Assert.Contains("<th>Projects</th>", html, StringComparison.Ordinal);
        Assert.Contains("name='maxWorkers' value='6'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/worker-types?project=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class='pooltypes'", html, StringComparison.Ordinal);
        // 1004-C (18:51): the pool's total is in the panel's summary, no longer in the title bar.
        Assert.DoesNotContain("of 6 workers", html, StringComparison.Ordinal);
    }

    // 1087-C (WT-P3): the panel built from the pool's own types (no project page carries them) fills
    // its boxes from the pool file: a type with a model shows it as the Model box's value, with its
    // endpoint and metrics; one with a blank model shows the effective model the Host
    // resolved greyed as the placeholder, the value still empty so an untouched Save writes no model.
    [Fact]
    public void MP_P7_PoolWorkerTypes_ModelFromThePoolFile_ValueAndPlaceholder()
    {
        var pool = new PoolPageSnapshot
        {
            Built = Built,
            MaxWorkers = 6,
            Running = 0,
            Types =
            [
                new PoolTypeView { Name = "Sonnet", Max = 2, Agent = "claude", Model = "claude-sonnet-5-5", Endpoint = "http://claude.local:8080/v1", MetricsUrl = "http://claude.local:9090/metrics" },
                new PoolTypeView { Name = "Spark", Max = 4, Agent = "qwen", Model = "", EffectiveModel = "qwen3.8-1m" },
            ],
            Pages = [AmigaX() with { WorkerTypes = [] }, Other() with { WorkerTypes = [] }],
        };
        var html = new CombinedPageRenderer().Render(pool, remote: false, controlsAllowed: true);

        Assert.Contains("name='wmodel' value='claude-sonnet-5-5' size='14'", html, StringComparison.Ordinal);
        Assert.Contains("name='wendpoint' value='http://claude.local:8080/v1'", html, StringComparison.Ordinal);
        Assert.Contains("name='wmetrics' value='http://claude.local:9090/metrics'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("wpremium", html, StringComparison.Ordinal); // 1146-A
        Assert.Contains("name='wmodel' value='' size='14' placeholder='qwen3.8-1m'", html, StringComparison.Ordinal);
    }

    // Section 4 items 2, 7 and 8, 1004-C: each project's planner switch is in its row, its line in the
    // switch's tip; its notice is in the notice area above the table, with its chip; the Recent events worker column names the
    // type and slot (950-C).
    [Fact]
    public void MP_P4_PlannerNoticeAndEventWorker_PerProject()
    {
        var amiga = AmigaX() with
        {
            PlannerLine = "planner: 2 cards queued, push: on",
            Notice = "120-A: the push was refused",
            Events = [new PageEvent { Time = Built.AddMinutes(-3), Text = "100-A done", Tone = PageTone.Ok, Type = "opus", Slot = 1, Model = "claude-opus-4" }],
        };
        var html = new CombinedPageRenderer().Render([amiga, Other()], Theme, remote: false, controlsAllowed: true);

        Assert.DoesNotContain("class='plannerrow'", html, StringComparison.Ordinal);
        Assert.Contains("data-tip='planner: 2 cards queued, push: on. This project&#39;s planner outbox is on.", Row(html, "AmigaX"), StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='project' value='AmigaX'><button class='pressed ok' aria-label='Planner push: on.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action='/planner-toggle'", Row(html, "Other"), StringComparison.Ordinal);
        // 1007-C: the notice is in the notice area above the table, with the project's chip in front,
        // and the Projects table keeps one row per project.
        Assert.DoesNotContain("noticerow", html, StringComparison.Ordinal);
        var notice = html.IndexOf("<div class='notice' data-project='AmigaX'><span class='chip projchip'", StringComparison.Ordinal);
        var tableStart = html.IndexOf("<table class='projects'>", StringComparison.Ordinal);
        Assert.True(notice >= 0 && notice < tableStart);
        Assert.Contains("</span> 120-A: the push was refused</div>", html[notice..tableStart], StringComparison.Ordinal);
        var table = html[tableStart..html.IndexOf("</table>", tableStart, StringComparison.Ordinal)];
        Assert.DoesNotContain("120-A", table, StringComparison.Ordinal);
        Assert.Equal(1, table.Split("<tr data-project='AmigaX'").Length - 1);
        Assert.DoesNotContain("opus slot 1", html, StringComparison.Ordinal);
    }

    // 957-B/990-B kept in a pool: the mask names the project and keys a pool worker project:slot, so a
    // click in one project never hides another project's same card or slot.
    [Fact]
    public void MP_P3_PendingMask_KeyedByProject()
    {
        Assert.Contains("function ofProject(r, project) { return !project || r.getAttribute('data-project') === project; }", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("if (project && slot) { slot = project + ':' + slot; }", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("project: project, card: cardOf(body.get('card'))", PageScript.Html, StringComparison.Ordinal);
    }

    // 1004-C (the developer, 2026-11-20 18:49 item 1): every row shows Check main from the first draw.
    // While a check runs it is pressed and cannot be pressed again, but still reads Check main and is
    // drawn at full strength; the health cell beside it names the sha being checked.
    [Fact]
    public void Card1004_CheckMain_OnEveryRow_EvenWhileACheckRuns()
    {
        var checking = Other() with { Health = new PageHealthView { Green = false, Sha = "9f8e7d6", Text = "main 9f8e7d6 RED at 12:10; checking abcdef1 (2 min)" } };
        var html = new CombinedPageRenderer().Render([AmigaX(), checking], Theme, remote: false, controlsAllowed: true);

        Assert.Contains(">Check main</button></form>", Row(html, "AmigaX"), StringComparison.Ordinal);
        Assert.Contains("<button class='checking' disabled>Checking...</button>", Row(html, "Other"), StringComparison.Ordinal);
        Assert.DoesNotContain("Checking...", Row(html, "AmigaX"), StringComparison.Ordinal);
        Assert.Contains("#w-projects button.checking[disabled] { opacity: 1; cursor: default; }", html, StringComparison.Ordinal);
    }

    // 1004-C (18:49 item 2): a button column is only as wide as its button, so the health column takes
    // the rest of the window and the buttons keep their normal spacing.
    [Fact]
    public void Card1004_ButtonColumns_AsWideAsTheirButtons()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);

        foreach (var row in new[] { Row(html, "AmigaX"), Row(html, "Other") })
        {
            Assert.Equal(6, Regex.Matches(row, "<td class='nw rowbtn'>").Count);
            Assert.Contains("<td class='wrap'>", row, StringComparison.Ordinal);
        }

        Assert.Contains("table.projects td.rowbtn { width: 1%; padding: 2px 3px; }", html, StringComparison.Ordinal);
    }

    // 1004-C (18:49 items 3 and 4): Drain has Stop's red look, in the rows and the title bar; Pause is
    // yellow while the project runs and Resume green while it is paused, and so are Pause all / Resume all.
    [Fact]
    public void Card1004_DrainRed_PauseYellow_ResumeGreen()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);
        var bar = Regex.Match(html, "id='w-projects'[^>]*><div class='bar'>(.*?)</div><div class='inner'>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains("<button class='bad' aria-label='Drain.", Row(html, "AmigaX"), StringComparison.Ordinal);
        Assert.Contains("<button class='bad' aria-label='Drain.", Row(html, "Other"), StringComparison.Ordinal);
        Assert.Contains("<button class='bad' aria-label='Drain and exit.", bar, StringComparison.Ordinal);
        Assert.Contains("<button class='warn' aria-label='Pause.", Row(html, "AmigaX"), StringComparison.Ordinal);
        Assert.Contains("<button class='pressed ok' aria-label='Resume.", Row(html, "Other"), StringComparison.Ordinal);
        Assert.Contains("<button class='warn' aria-label='Pause all.", bar, StringComparison.Ordinal);
        Assert.Contains("#w-projects button.warn { color: var(--warn); } #w-projects button.ok { color: var(--ok); }", html, StringComparison.Ordinal);

        var paused = new CombinedPageRenderer().Render([AmigaX() with { State = Other().State }, Other()], Theme, remote: false, controlsAllowed: true);
        Assert.Contains("<button class='pressed ok' aria-label='Resume all.", paused, StringComparison.Ordinal);
    }

    // 1004-C (18:49 item 6): a row's health shows one state, in its own colour: green, red, grey when
    // main is not checked yet, and only "checking <sha>" (grey) while a check runs.
    [Theory]
    [InlineData("1a2b3c4", true, "main 1a2b3c4 green at 12:20", "ok", "main 1a2b3c4 green at 12:20")]
    [InlineData("9f8e7d6", false, "main 9f8e7d6 RED at 12:10", "bad", "main 9f8e7d6 RED at 12:10")]
    [InlineData("", true, "main not checked yet", "mute", "main not checked yet")]
    [InlineData("9f8e7d6", false, "main 9f8e7d6 RED at 12:10; checking abcdef1 (2 min)", "mute", "checking abcdef1 (2 min)")]
    [InlineData("", true, "main not checked yet; checking abcdef1 (0 min)", "mute", "checking abcdef1 (0 min)")]
    public void Card1004_Health_OneStateWithItsColour(string sha, bool green, string text, string tone, string shown)
    {
        var project = Other() with { Health = new PageHealthView { Green = green, Sha = sha, Text = text } };
        var html = new CombinedPageRenderer().Render([AmigaX(), project], Theme, remote: false, controlsAllowed: true);
        var cell = Regex.Match(Row(html, "Other"), "<td class='wrap'>(.*?)</td>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal("<span class='" + tone + "'>" + shown + "</span>", cell);
    }

    // 1004-C (18:51 item 8): the title bar keeps the name and version, the PC's load, the dropdown, the
    // theme, the last update and its buttons; no state, health or workers summary.
    [Fact]
    public void Card1004_TitleBar_NoStateHealthOrWorkersSummary()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);
        var title = Regex.Match(html, "id='w-projects'[^>]*><div class='bar'><b>(.*?)</b>", RegexOptions.Singleline).Groups[1].Value;

        Assert.StartsWith("Switchyard v", title, StringComparison.Ordinal);
        foreach (var gone in new[] { "running", "paused", "main ", "workers", "projects" })
        {
            Assert.DoesNotContain(gone, title, StringComparison.Ordinal);
        }
    }

    // 1009-C: the pool page is "Switchyard": the title bar reads "Switchyard v<version>  --  CPU ...", and so does <title>.
    // 1103-C: that CPU and memory text sits in the element the page script updates from the live stream's
    // frames, so the words are the same and they now live inside <span id='hostload'>.
    [Fact]
    public void Card1009_TitleBarAndTabTitle_AreSwitchyard()
    {
        var load = new HostLoadSample { CpuPercent = 22, FreeMemoryBytes = 6L * 1024 * 1024 * 1024, TotalMemoryBytes = 31L * 1024 * 1024 * 1024, Cores = 32 };
        var html = new CombinedPageRenderer().Render([AmigaX() with { HostLoad = load }, Other()], Theme, remote: false, controlsAllowed: true);
        var title = Regex.Match(html, "id='w-projects'[^>]*><div class='bar'><b>(.*?) <span id='live-state'>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal(
            "Switchyard v" + System.Net.WebUtility.HtmlEncode(ViewsFixture.ShownVersion)
            + " &nbsp;-- &nbsp;<span id='hostload'>CPU 22%, 6G free of 31G, 32 threads</span>",
            title);
        Assert.Contains("<title>Switchyard</title>", html, StringComparison.Ordinal);
    }

    // 1004-C (18:49 items 5 and 7): the page is not rebuilt while the project dropdown is open (as the
    // theme menu), and Worker types has its fields and opens: a refresh puts back the open state this
    // browser keeps, where it used to draw the panel closed again.
    [Fact]
    public void Card1004_DropdownKeptOpen_WorkerTypesOpensAndStaysOpen()
    {
        var html = new CombinedPageRenderer().Render(
            [AmigaX() with { WorkerTypes = [new PageWorkerTypeView { Name = "opus", Max = 2 }] }, Other()], Theme, remote: false, controlsAllowed: true);

        Assert.Contains("#themesel:focus, #projsel:focus, #viewsel:focus')) return true;", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("<button type='button' class='wttoggle' data-key='wtypes-open'", html, StringComparison.Ordinal);
        Assert.Matches("<div class='wtbody'>.*<table class='wtypes'>.*name='wname' value='opus'", html);
        // In refresh, after the desk is replaced: the logs' sizes and then the Worker types' open state.
        Assert.Matches(@"applyLogs\(\);\s*(//[^\n]*\s*)?applyWt\(\);\s*pinLogScroll\(\);\s*applyHl\(\);", PageScript.Html);
    }

    // 1004-C (18:57 item 13): a worker's window is titled by its type and card (the chip first with two
    // or more projects); Minimize collapses it as on the single page (the same toggle, keyed project:slot);
    // Stop and Kill have the red stop style.
    [Fact]
    public void Card1004_WorkerWindow_TitleMinimizeAndRedStopKill()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);
        var win = Regex.Match(html, "<section class='win' data-project='AmigaX' data-card='120-A'>.*?</section>", RegexOptions.Singleline).Value;

        Assert.Contains("<b>" + WorkerChip("AmigaX") + " opus (claude-opus-4) - ", win, StringComparison.Ordinal);
        Assert.Contains("<button type='button' class='logtoggle' data-slot='AmigaX:1' title='Show only the title bar'>Minimize</button>", win, StringComparison.Ordinal);
        Assert.Contains("<button class='bad' aria-label='Stop.", win, StringComparison.Ordinal);
        Assert.Contains("<button class='bad' aria-label='Kill.", win, StringComparison.Ordinal);

        var alone = new CombinedPageRenderer().Render([AmigaX()], Theme, remote: false, controlsAllowed: true);
        Assert.Matches(@"<b><span class='chip projchip wchip'[^>]*title='AmigaX'>AmigaX</span> opus \(claude-opus-4\) - <a href='/card\?project=AmigaX&name=120-A-dispatch.md'>", alone);
        Assert.Contains(".wchip { display: inline-block !important; }", alone, StringComparison.Ordinal);
    }

    // 1008-C: Minimize collapses the log only. The summary (the time-limit bar, the spinner with the
    // throughput, and the minutes) sits in the title bar, outside the collapsing inner part, as on the
    // single page.
    [Fact]
    public void Card1008_MinimizedWorkerWindow_KeepsTimeThroughputSpinnerAndBar()
    {
        var slots = AmigaX().Slots
            .Select(slot => slot with { TimeLimit = 40, Stats = "context ~2k  |  12.5 avg  |  " + slot.Model })
            .ToList();
        var html = new CombinedPageRenderer().Render([AmigaX() with { Slots = slots }, Other()], Theme, remote: false, controlsAllowed: true);
        var win = Regex.Match(html, "<section class='win' data-project='AmigaX' data-card='120-A'>.*?</section>", RegexOptions.Singleline).Value;
        var bar = Regex.Match(win, "<div class='bar'>.*?</div><div class='inner'>", RegexOptions.Singleline).Value;

        Assert.Contains("<span class='prog' data-slot='AmigaX:1' style='width:30%'></span>", bar, StringComparison.Ordinal);
        Assert.Contains("<span class='spin'></span><span class='tps'>12.5 T/s avg</span>", bar, StringComparison.Ordinal);
        Assert.Contains("<span class='tag' data-slot='AmigaX:1'>12/40 min</span>", bar, StringComparison.Ordinal);
        Assert.Contains("class='logtoggle' data-slot='AmigaX:1'", bar, StringComparison.Ordinal);
        Assert.Contains(".win.mini > .inner { display: none; }", html, StringComparison.Ordinal);
    }

    // 1024-C: a pool worker window with no saved size starts minimized; a saved size (either) wins and
    // is kept per worker. The single page's windows (no data-project) still start full.
    [Fact]
    public void Card1024_PoolWorkerWindow_StartsMinimizedUnlessSaved()
    {
        var html = new CombinedPageRenderer().Render([AmigaX(), Other()], Theme, remote: false, controlsAllowed: true);
        var win = Regex.Match(html, "<section class='win' data-project='AmigaX' data-card='120-A'>.*?</section>", RegexOptions.Singleline).Value;

        Assert.Contains("class='logtoggle' data-slot='AmigaX:1'", win, StringComparison.Ordinal);
        // No saved state -> the default for a window that carries data-project is mini.
        Assert.Contains("w.hasAttribute('data-project') ? 'mini' : 'full'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("return v === 'mini' || v === 'full' ? v : (def || 'full');", PageScript.Html, StringComparison.Ordinal);
        // A saved 'full' (expanded) is stored and honoured, and the toggle flips from the effective size.
        Assert.Contains("localStorage.setItem('log-mode-' + s, v)", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("nextMode[logMode(s, winDefault(b))]", PageScript.Html, StringComparison.Ordinal);
    }

    /// <summary>Setup's project P: a question card, a review card, and runs on two models.</summary>
    private static PageSnapshot AmigaX() => new()
    {
        Built = Built,
        Project = "AmigaX",
        Version = ViewsFixture.ShownVersion,
        Theme = "modern",
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
        Health = new PageHealthView { Green = true, Text = "main 1a2b3c4 green at 12:20" },
        Stats = new PageStats
        {
            Cards = 4,
            Minutes = 305,
            Text = "Last 24 h (5 h 5 min): 4 cards (5 runs): done 3, 1 in review, 0 waiting on questions, 0 open; 0.2 cards/hr\n"
                 + "  opus (3 h 5 min): 2 cards (3 runs): done 2, 0 in review, 0 waiting on questions, 0 open; 0.1 cards/hr\n"
                 + "  gpt (2 h 0 min): 2 cards (2 runs): done 1, 1 in review, 0 waiting on questions, 0 open; 0.1 cards/hr",
        },
        Waiting =
        [
            new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "120-A-dispatch.md", Note = "which window?" },
            new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "090-C-page-views.md", Note = "look at the page" },
        ],
        Slots =
        [
            new PageSlotView
            {
                Slot = 1,
                Enabled = true,
                WorkerName = "opus",
                Model = "claude-opus-4",
                Card = "120-A-dispatch.md",
                Start = Built.AddMinutes(-12),
            },
        ],
        Queue =
        [
            new PageQueueItem { Name = "350-A-pool.md", Title = "pool", Note = "next up" },
        ],
        Events =
        [
            new PageEvent { Time = Built.AddMinutes(-3), Text = "100-A done", Tone = PageTone.Ok },
        ],
    };

    /// <summary>Setup's project O: a question card, a review card, and runs on two models.</summary>
    private static PageSnapshot Other() => new()
    {
        Built = Built.AddMinutes(-1),
        Project = "Other",
        Version = ViewsFixture.ShownVersion,
        Theme = "amber",
        State = new PageStateView { State = SchedulerState.Paused, Text = "paused" },
        Health = new PageHealthView { Green = false, Text = "main 9f8e7d6 red at 12:10" },
        Stats = new PageStats
        {
            Cards = 2,
            Minutes = 80,
            Text = "Last 24 h (1 h 20 min): 2 cards (2 runs): done 1, 1 in review, 0 waiting on questions, 0 open; 0.1 cards/hr\n"
                 + "  sonnet (1 h 20 min): 2 cards (2 runs): done 1, 1 in review, 0 waiting on questions, 0 open; 0.1 cards/hr",
        },
        Waiting =
        [
            new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "020-B-git.md", Note = "which remote?" },
            new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "080-B-config.md", Note = "the git retry needs a look" },
        ],
        Slots =
        [
            new PageSlotView
            {
                Slot = 2,
                Enabled = true,
                WorkerName = "sonnet",
                Model = "gpt-5",
                Card = "020-B-git.md",
                Start = Built.AddMinutes(-4),
            },
        ],
        Queue =
        [
            new PageQueueItem { Name = "360-B-pool-host.md", Title = "pool host", Note = "waiting on 350-A" },
        ],
        Events =
        [
            new PageEvent { Time = Built.AddMinutes(-6), Text = "010-B started", Tone = PageTone.Warn },
        ],
    };
}
