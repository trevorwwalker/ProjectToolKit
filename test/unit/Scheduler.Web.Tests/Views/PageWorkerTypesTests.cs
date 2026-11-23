using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 890-C: the page half of worker types (worker-types.md section 4). The worker windows are
/// per running worker with nothing left to disable (WT-P1, WT-P2), and the Worker types panel
/// carries what the per-worker Disable and Settings buttons used to (WT-P3).
/// </summary>
public sealed class PageWorkerTypesTests
{
    private static string Render(PageSnapshot? snapshot = null, bool controlsAllowed = true, int? maxWorkers = null) =>
        new PageRenderer(maxWorkers: maxWorkers is { } total ? () => total : null)
            .Render(snapshot ?? ViewsFixture.Full(), remote: !controlsAllowed, controlsAllowed);

    // The part of the page that is worker n's window.
    private static string WindowOf(string html, int slot)
    {
        var start = html.IndexOf($"<a class='barlink' href='/log?slot={slot}'>", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no window for worker {slot}");
        start = html.LastIndexOf("<section class='win'", start, StringComparison.Ordinal);
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>
    /// WT-P1: one window per running worker, titled by its type, slot and card, with Minimize, Stop
    /// and Kill and the message box - and no Disable, Enable or Settings, which belonged to a slot.
    /// An idle folder has no window at all.
    /// </summary>
    [Fact]
    public void Page_WorkerWindows_OnlyRunningWorkers_StopKillNoDisable()
    {
        var full = ViewsFixture.Full();
        var idle = full.Slots[0] with { Card = "", Start = null, LogTail = [] };
        var html = Render(full with { Slots = [idle, full.Slots[1]] });

        Assert.False(html.Contains("<a class='barlink' href='/log?slot=1'>", StringComparison.Ordinal), "an idle folder has a window");
        var two = WindowOf(html, 2);
        Assert.Contains("Sonnet - slot 2", two, StringComparison.Ordinal);
        Assert.Contains("090-C-page-views.md</a>", two, StringComparison.Ordinal);
        Assert.Contains(">Minimize</button>", two, StringComparison.Ordinal);
        Assert.Contains(">Kill</button>", two, StringComparison.Ordinal);
        Assert.DoesNotContain(">Stop</button>", two, StringComparison.Ordinal); // already stopping (TIME-10)
        Assert.Contains("action='/send?slot=2'", two, StringComparison.Ordinal);
        Assert.DoesNotContain("Disable", two, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable", two, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings", two, StringComparison.Ordinal);
        Assert.DoesNotContain("/disable", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/enable", html, StringComparison.Ordinal);
        Assert.DoesNotContain("opensettings", html, StringComparison.Ordinal);
    }

    // A running worker that is not stopping has both Stop and Kill, in the developer's order.
    [Fact]
    public void Page_WorkerWindow_StopBeforeKill()
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

    // WT-P1: with nothing running the window area is one line that says how many places there are.
    [Fact]
    public void Page_WorkerWindows_NoneRunning_SaysNoneOfTheTotal()
    {
        var html = Render(ViewsFixture.Full() with { Slots = [] }, maxWorkers: 4);

        Assert.Contains("No workers running (0 of 4)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a class='barlink' href='/log?slot=", html, StringComparison.Ordinal);
    }

    // WT-C1: the pool-wide limit is labelled Max active workers, not Total workers.
    [Fact]
    public void Page_WorkerTypes_LabelsTheLimitMaxActiveWorkers()
    {
        var html = Render(maxWorkers: 4);

        Assert.Contains("Max active workers <input name='maxWorkers'", html, StringComparison.Ordinal);
        Assert.Contains("the most workers that run at once, across all types", html, StringComparison.Ordinal);
        Assert.Contains("the caps share the max.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Total workers", html, StringComparison.Ordinal);
    }

    // WT-13: a worker whose type has left the config keeps its window and says so.
    [Fact]
    public void Page_WorkerWindow_TypeGoneFromTheConfig_SaysRemoved()
    {
        var full = ViewsFixture.Full();
        var html = Render(full with { WorkerTypes = [full.WorkerTypes[1]] });

        Assert.Contains("worker (removed) - slot 1", html, StringComparison.Ordinal);
        Assert.Contains("Sonnet - slot 2", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// WT-P3: the panel's rows are the config's order, each with its running-of-cap, its cap, agent,
    /// model, endpoint, metrics, premium and areas; cap 0 is how a type is off; Add type, Remove and
    /// Save are there; and the whole panel posts to /worker-types.
    /// </summary>
    [Fact]
    public void Page_WorkerTypesPanel_RowsInOrder_RunningOfMax_OffAtZero()
    {
        var html = Render();
        var panel = html[html.IndexOf("<form method='post' action='/worker-types'", StringComparison.Ordinal)..];
        var rows = panel.Split("<tr class='wtyperow'>", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5, rows.Length); // the header, the three types, and the row Add type copies

        // In the config's order (WT-C5), each where it is in the panel.
        var worker = rows[1];
        var sonnet = rows[2];
        var opus = rows[3];
        Assert.Contains("name='wname' value='worker'", worker, StringComparison.Ordinal);
        Assert.Contains(">2 of 2</span>", worker, StringComparison.Ordinal);
        Assert.Contains("name='wmax' value='2'", worker, StringComparison.Ordinal);
        Assert.Contains(">0 of 1</span>", sonnet, StringComparison.Ordinal);
        Assert.Contains("name='wagent'", sonnet, StringComparison.Ordinal);
        Assert.Contains("<option value='claude' selected>", sonnet, StringComparison.Ordinal);
        Assert.Contains("name='wmodel' value='claude-sonnet-5-5'", sonnet, StringComparison.Ordinal);
        Assert.Contains("name='wendpoint' value='http://claude.local:8080/v1'", sonnet, StringComparison.Ordinal);
        Assert.Contains("name='wmetrics' value='http://claude.local:9090/metrics'", sonnet, StringComparison.Ordinal);
        Assert.Contains("<option value='1' selected>yes</option>", sonnet, StringComparison.Ordinal);

        // Cap 0 is off: the row says "0 of 0", and its Remove works because none of it runs.
        Assert.Contains(">0 of 0</span>", opus, StringComparison.Ordinal);
        Assert.Contains("name='wmax' value='0'", opus, StringComparison.Ordinal);
        Assert.Contains("class='typeremove' title=", opus, StringComparison.Ordinal);
        Assert.Contains("class='typeremove' disabled", worker, StringComparison.Ordinal); // 2 of 2 running

        Assert.Contains("<input name='maxWorkers' value='3'", html, StringComparison.Ordinal);
        Assert.Contains("class='addtype'", html, StringComparison.Ordinal);
        Assert.Contains("<template id='wtype-new'", html, StringComparison.Ordinal);
        Assert.Contains("button.addtype", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("button.typeremove", PageScript.Html, StringComparison.Ordinal);
    }

    // WT-9: an out-of-quota agent shows since when, and has a Resume that posts /agent-resume for it.
    [Fact]
    public void Page_WorkerTypesPanel_AgentOutOfQuota_ShowsSinceAndResume()
    {
        var full = ViewsFixture.Full();
        var since = new DateTimeOffset(2026, 11, 19, 9, 40, 0, TimeSpan.Zero);
        var html = Render(full with
        {
            WorkerTypes =
            [
                full.WorkerTypes[0],
                full.WorkerTypes[1] with { OutOfQuotaSince = since },
                full.WorkerTypes[2],
            ],
        });

        Assert.Contains("out of quota since 09:40", html, StringComparison.Ordinal);
        Assert.Contains("action='/agent-resume?agent=claude'", html, StringComparison.Ordinal);
        Assert.Contains(">Resume</button>", html, StringComparison.Ordinal);
    }

    // The panel offers the project's agents and the default (WT-P3, CFG-10).
    [Fact]
    public void Page_WorkerTypesPanel_OfferstheAgentsAndTheDefault()
    {
        var html = new PageRenderer(agents: () => ["copilot", "claude"])
            .Render(ViewsFixture.Full(), remote: false, controlsAllowed: true);
        var worker = html.Split("<tr class='wtyperow'>", StringSplitOptions.RemoveEmptyEntries)[1];

        Assert.Contains("<option value='' selected>default</option>", worker, StringComparison.Ordinal);
        Assert.Contains("<option value='copilot'>copilot</option>", worker, StringComparison.Ordinal);
        Assert.Contains("<option value='claude'>claude</option>", worker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 947-B: each row carries the name it was loaded as in a hidden field, so the Save can tell a
    /// renamed type from one row removed and another added; the row Add type copies carries none,
    /// because it is a new type. The visible name field is unchanged.
    /// </summary>
    [Fact]
    public void Page_WorkerTypesPanel_EveryRowCarriesItsLoadedName()
    {
        var html = Render();
        var panel = html[html.IndexOf("<form method='post' action='/worker-types'", StringComparison.Ordinal)..];
        var rows = panel.Split("<tr class='wtyperow'>", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5, rows.Length);
        Assert.Contains("<input type='hidden' name='worig' value='worker'>", rows[1], StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='worig' value='Sonnet'>", rows[2], StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='worig' value='Opus'>", rows[3], StringComparison.Ordinal);
        Assert.Contains("<input type='hidden' name='worig' value=''>", rows[4], StringComparison.Ordinal);
        Assert.Contains("<input name='wname' value='worker' size='10'>", rows[1], StringComparison.Ordinal);
    }

    // PAGE-7: read from another PC the panel is there but nothing in it can be used.
    [Fact]
    public void Page_WorkerTypesPanel_WithoutControls_IsDisabled()
    {
        var html = Render(controlsAllowed: false);
        var panel = html[html.IndexOf("<form method='post' action='/worker-types'", StringComparison.Ordinal)..];

        Assert.Contains("name='wname' value='worker' size='10' disabled", panel, StringComparison.Ordinal);
        Assert.Contains("class='addtype' disabled", panel, StringComparison.Ordinal);
        Assert.Contains("<button disabled title='Write these types", panel, StringComparison.Ordinal);
    }

    // The windows carry the project. 1004-C: the pool has one Worker types panel, the pool's, whose
    // Save names no project (it writes the pool file, 1000-B), where each project had its own.
    [Fact]
    public void Page_Combined_WorkerWindows_OnePoolPanel()
    {
        var html = new CombinedPageRenderer()
            .Render([ViewsFixture.Full() with { Project = "Kestrel" }], "Modern", remote: false, controlsAllowed: true);

        // The window is keyed as the single page keys its rows (number and letter), for the pending mask (957-B).
        Assert.Contains("<section class='win' data-project='Kestrel' data-card='070-A'>", html, StringComparison.Ordinal);
        // 1004-C (18:57 item 13a): titled by type (and model) and card, linked to its view; no slot number.
        Assert.Matches(@"<b><span class='chip projchip wchip'[^>]*>Kestrel</span> worker( \([^)]*\))? - <a href='/card\?project=Kestrel&name=070-A", html);
        Assert.DoesNotContain("slot 1", html, StringComparison.Ordinal);
        Assert.Contains("action='/stop' class='g' data-tip='Ask this worker to stop at its next safe point; the card then waits under Waiting on you with Resume.'><input type='hidden' name='project' value='Kestrel'><input type='hidden' name='slot' value='1'>", html, StringComparison.Ordinal);
        Assert.Contains("action='/kill' class='g' data-tip='End this worker and everything it started, now. The card waits under Waiting on you with Resume.'><input type='hidden' name='project' value='Kestrel'><input type='hidden' name='slot' value='1'>", html, StringComparison.Ordinal);
        Assert.Contains("action='/worker-types' class='wtypes wthidden'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/worker-types?project=", html, StringComparison.Ordinal);
        Assert.Contains("data-slot='Kestrel:1'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class='workers'", html, StringComparison.Ordinal);
    }

    // MP-8, WT-P1: with none running the combined page still says how many it has - 1004-C: in the
    // Worker types summary (the title bar's count was taken out, 18:51), and no worker window is drawn.
    [Fact]
    public void Page_Combined_NoneRunning_SaysNoneOfTheTotal()
    {
        var html = new CombinedPageRenderer()
            .Render([ViewsFixture.Full() with { Project = "Kestrel", Slots = [] }], "", remote: false, controlsAllowed: true);

        // 1004-C (18:51): the count left the title bar; the Worker types summary carries the total.
        Assert.DoesNotContain("of 3 workers", html, StringComparison.Ordinal);
        Assert.Contains("max 3 active</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<section class='win' data-project='Kestrel'", html, StringComparison.Ordinal);
    }

    /// <summary>Card 991-C: the Workers button is gone; the panel is the one place for those settings.</summary>
    [Fact]
    public void Page_NoWorkersButton()
    {
        var html = Render();

        Assert.DoesNotContain(">Workers</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/open-workers", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Card 991-C: the heading is a toggle that starts hidden, with a one-line summary; the fields
    /// stay in the form (hiding keeps unsaved edits); the shown state is read from the browser's
    /// stored setting.
    /// </summary>
    [Fact]
    public void Page_WorkerTypesPanel_ToggleSummaryAndStoredState()
    {
        var html = Render(maxWorkers: 4);

        Assert.Contains("class='wttoggle'", html, StringComparison.Ordinal);
        Assert.Contains("Worker types <span class='wtarrow'>", html, StringComparison.Ordinal);
        Assert.Contains("class='wtypes wthidden'", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mute wtsum'>worker 2 of 2, Sonnet 0 of 1, Opus 0 of 0 - max 4 active</span>", html, StringComparison.Ordinal);
        Assert.Contains("name='maxWorkers'", html, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem(k) === 'open'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='wtypes-open'", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// 1087-C (WT-P3): a type whose own model is blank shows the model it really runs - the agent's
    /// default, or the name its server alias serves - greyed as the Model box's placeholder; the
    /// value stays empty, so a Save of an untouched row still writes no model. A type with a model
    /// shows it as the value and needs no placeholder.
    /// </summary>
    [Fact]
    public void Page_WorkerTypesPanel_BlankModel_ShowsEffectiveAsPlaceholder_EmptyValue()
    {
        PageWorkerTypeView[] types =
        [
            new() { Name = "Spark", Max = 4, Agent = "qwen", Model = "", EffectiveModel = "qwen3.8-1m" },
            new() { Name = "Sonnet", Max = 1, Agent = "claude", Model = "claude-sonnet-5-5", EffectiveModel = "claude-sonnet-5-5" },
        ];
        var html = Render(ViewsFixture.Full() with { WorkerTypes = types });
        var panel = html[html.IndexOf("<form method='post' action='/worker-types'", StringComparison.Ordinal)..];
        var rows = panel.Split("<tr class='wtyperow'>", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, rows.Length); // the header, the two types, and the row Add type copies
        Assert.Contains("name='wmodel' value='' size='14' placeholder='qwen3.8-1m'", rows[1], StringComparison.Ordinal);
        Assert.Contains("name='wmodel' value='claude-sonnet-5-5' size='14'", rows[2], StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder='claude-sonnet-5-5'", rows[2], StringComparison.Ordinal);
    }
}
