using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 480-C: the controls the old page (schedule.ps1 at c919b43) had and the new one lacked, one
/// test each. The script is read, not run: each test checks that the control is on the rendered
/// page and wired to its route or to the script that drives it.
/// </summary>
public sealed class PageParityTests
{
    private static string Render(PageSnapshot? snapshot = null, bool controlsAllowed = true) =>
        new PageRenderer().Render(snapshot ?? ViewsFixture.Full(), remote: !controlsAllowed, controlsAllowed);

    // The part of the page that is worker n's window.
    private static string WindowOf(string html, int slot)
    {
        var start = html.IndexOf($"<a class='barlink' href='/log?slot={slot}'>", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no window for worker {slot}");
        start = html.LastIndexOf("<section class='win'", start, StringComparison.Ordinal);
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    // Worker windows have Minimize/Expand, remembered per browser as the old page did ('log-mode-n').
    [Fact]
    public void Page_WorkerWindow_MinimizeToggle()
    {
        var html = Render();

        foreach (var slot in new[] { 1, 2 })
        {
            Assert.Contains($"<button type='button' class='logtoggle' data-slot='{slot}' title='Show only the title bar'>Minimize</button>", WindowOf(html, slot), StringComparison.Ordinal);
        }

        Assert.Contains("'log-mode-'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("button.logtoggle", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("classList.add('mini')", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("'Expand'", PageScript.Html, StringComparison.Ordinal);
    }

    // A minimised window keeps one line: the card, a spinner, the worker's average T/s, its minutes.
    [Fact]
    public void Page_WorkerWindow_MiniLine()
    {
        var full = ViewsFixture.Full();
        var snapshot = full with { Slots = [full.Slots[0] with { Stats = "~41 T/s now, 38.5 avg" }, full.Slots[1]] };
        var one = WindowOf(Render(snapshot), 1);

        Assert.Contains("<span class='mini' data-slot='1'><span class='spin'></span><span class='tps'>38.5 T/s avg</span></span>", one, StringComparison.Ordinal);
        Assert.Contains("<span class='tag' data-slot='1'>12 min</span>", one, StringComparison.Ordinal);
        Assert.Contains("070-A-config-model.md</a>", one, StringComparison.Ordinal);
        Assert.Contains("<span class='tps'>- T/s</span>", WindowOf(Render(), 1), StringComparison.Ordinal);
        Assert.Contains("applyMini", PageScript.Html, StringComparison.Ordinal);
    }

    // Title-bar buttons, in the developer's order: Minimize/Expand, Stop, Kill (WT-P1). The slot's
    // Disable went with the slots; the type's settings are in the Worker types panel.
    [Fact]
    public void Page_WorkerWindow_ButtonOrder()
    {
        var one = WindowOf(Render(), 1);
        var at = new[] { ">Minimize</button>", ">Stop</button>", ">Kill</button>" }
            .Select(label => one.IndexOf(label, StringComparison.Ordinal))
            .ToList();

        Assert.All(at, i => Assert.True(i >= 0, string.Join(",", at)));
        Assert.True(at[0] < at[1] && at[1] < at[2], string.Join(",", at));
        Assert.Contains("action='/stop?slot=1'", one, StringComparison.Ordinal);
        Assert.Contains("action='/kill?slot=1'", one, StringComparison.Ordinal);
    }

    // WT-P1, WT-P2: no window carries a control that belonged to a slot.
    [Fact]
    public void Page_WorkerWindow_CarriesNoSlotControls()
    {
        var html = Render();

        foreach (var slot in new[] { 1, 2 })
        {
            var one = WindowOf(html, slot);
            Assert.DoesNotContain("Disable", one, StringComparison.Ordinal);
            Assert.DoesNotContain("Enable", one, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings", one, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("action='/enable", html, StringComparison.Ordinal);
        Assert.DoesNotContain("action='/disable", html, StringComparison.Ordinal);
    }

    // WT-P1: an idle folder has no window - nothing says idle, nothing offers a card, and there is
    // nothing to Stop or Kill.
    [Fact]
    public void Page_WorkerWindow_Idle()
    {
        var full = ViewsFixture.Full();
        var html = Render(full with { Slots = [full.Slots[0] with { Card = "", Start = null, LogTail = [] }, full.Slots[1]] });

        Assert.False(html.Contains("<a class='barlink' href='/log?slot=1'>", StringComparison.Ordinal), "the idle folder drew a window");
        Assert.DoesNotContain("No card.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">idle<", html, StringComparison.Ordinal);
        var two = WindowOf(html, 2);
        Assert.DoesNotContain(">Stop</button>", two, StringComparison.Ordinal); // it is already stopping
        Assert.Contains(">Kill</button>", two, StringComparison.Ordinal);
    }

    // A running worker's window carries its card, so a Depends-on link can light it up.
    [Fact]
    public void Page_WorkerWindow_CarriesItsCard()
    {
        var html = Render();

        Assert.Contains("<section class='win' data-card='070-A'>", html, StringComparison.Ordinal);
    }

    // The top bar's time reads as the page's update time (developer, 2026-11-18).
    [Fact]
    public void Page_TopBar_LastUpdate()
    {
        Assert.Contains($"<span class='updated'>last update {ViewsFixture.Built:HH:mm:ss}</span>", Render(), StringComparison.Ordinal);
    }

    // The appearance selector is wired: a choice applies at once and is kept in this browser, and
    // on load the stored theme is applied and selected (developer, 2026-11-18).
    [Fact]
    public void Page_ThemeSelector_Wired()
    {
        var html = Render();
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];

        Assert.Contains("e.target.id !== 'themesel') return;", head, StringComparison.Ordinal);
        Assert.Contains("if (setTheme(e.target.value)) storeTheme(e.target.value);", head, StringComparison.Ordinal);
        Assert.Contains("getElementById('themesel')", head, StringComparison.Ordinal);
        Assert.Contains("sel.value = k", head, StringComparison.Ordinal);
    }

    // WT-P3, WT-P4: what the per-worker Settings dialog was, the Worker types panel now is - in the
    // Control window, posting to /worker-types, its script wired.
    [Fact]
    public void Page_WorkerSettings_BecameTheWorkerTypesPanel()
    {
        var html = Render();

        Assert.DoesNotContain("<dialog", html, StringComparison.Ordinal);
        Assert.DoesNotContain("opensettings", html, StringComparison.Ordinal);
        Assert.Contains("<form method='post' action='/worker-types' class='wtypes wthidden'>", html, StringComparison.Ordinal);
        Assert.Contains("Worker types <span class='wtarrow'>", html, StringComparison.Ordinal);
        Assert.Contains("name='maxWorkers'", html, StringComparison.Ordinal);
        Assert.Contains("<button type='button' class='addtype'", html, StringComparison.Ordinal);
        Assert.Contains("<button type='button' class='typeremove'", html, StringComparison.Ordinal);
        Assert.Contains("button.addtype", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("button.typeremove", PageScript.Html, StringComparison.Ordinal);
    }

    // Read from another PC, the panel is drawn but every control in it is disabled (PAGE-7).
    [Fact]
    public void Page_WorkerSettings_DisabledRemotely()
    {
        var html = Render(controlsAllowed: false);

        Assert.Contains("<button disabled title='Write these types to workers.json", html, StringComparison.Ordinal);
        Assert.Contains("<input name='wname' value='worker' size='10' disabled>", html, StringComparison.Ordinal);
    }

    // Every "Waiting on you" row links its card and has Open, which opens it in the editor.
    [Fact]
    public void Page_Waiting_CardLinkAndOpen()
    {
        var html = Render();

        Assert.Contains("<tr data-card='030-A'>", html, StringComparison.Ordinal);
        Assert.Contains("<a href='/card?name=030-A'>030-A</a>", html, StringComparison.Ordinal);
        Assert.Contains("action='/open?card=030-A'", html, StringComparison.Ordinal);
        Assert.Contains("action='/open?card=080-B'", html, StringComparison.Ordinal);
    }

    // A question's choices answer it through one form that also carries an optional note.
    [Fact]
    public void Page_Waiting_QuestionFormWithNote()
    {
        var full = ViewsFixture.Full();
        var html = Render(full with
        {
            Waiting =
            [
                full.Waiting[2] with
                {
                    Questions = [full.Waiting[2].Questions[0] with { Choices = [new CardChoice { Label = "json", Destination = CardFolder.Done }, new CardChoice { Label = "yaml" }] }],
                },
            ],
        });

        // 1099-C: the card has one note form (its id is the card's), and the answers are buttons of it.
        Assert.Contains("<form id='n-020-B' method='post' action='/note' class='g'", html, StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='card' value='020-B'><input type='hidden' name='q' value='1'>", html, StringComparison.Ordinal);
        Assert.Contains("<input name='note' maxlength='500' placeholder='note (optional)'>", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-020-B' formaction='/answer?q=1' name='a' value='0' title='json; the card then goes to done/'>json</button>", html, StringComparison.Ordinal);
        Assert.Contains("<button form='n-020-B' formaction='/answer?q=1' name='a' value='1' title='yaml'>yaml</button>", html, StringComparison.Ordinal);
    }

    // A waiting row with no reason says where to look.
    [Fact]
    public void Page_Waiting_NoReason()
    {
        var full = ViewsFixture.Full();
        var html = Render(full with { Waiting = [full.Waiting[1] with { Note = "" }] });

        Assert.Contains("No reason given; see the card's Result.", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    // Queue rows carry their card (for the highlight) and its title as the link's tooltip.
    // 968-C: the tooltip carries the whole name too, as the cell can ellipsise it.
    [Fact]
    public void Page_Queue_RowsCarryCardAndTitle()
    {
        var html = Render();

        Assert.Contains("<tr data-card='100-A'>", html, StringComparison.Ordinal);
    Assert.Contains("<a href='/card?name=100-A-lanes-and-workers-json.md' title='lanes and workers.json - 100-A-lanes-and-workers-json.md'>", html, StringComparison.Ordinal);
    }

    // The planner's window counts its waiting cards in the title bar, and links each card.
    [Fact]
    public void Page_Requests_CountsAndLinksCards()
    {
        var html = Render();

        Assert.Contains("<span class='tag'>1 open, 2 cards</span>", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-card='060-E'><td><a href='/card?name=060-E'>060-E</a>", html, StringComparison.Ordinal);
    }

    // The page waits to refresh while the pointer is on a log, as the old page did.
    [Fact]
    public void Page_Refresh_WaitsWhileReadingALog()
    {
        Assert.Contains("pre.log:hover", PageScript.Html, StringComparison.Ordinal);
    }
}
