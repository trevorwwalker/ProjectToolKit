using System.Text;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;

namespace Scheduler.Web.Views;

/// <summary>
/// The page (PAGE-5): the notice, the red banner, the Control window (whose title bar carries the
/// scheduler's own line, the theme selector and the last update - there is no top bar since 520-A),
/// what is waiting on the developer, the planner's window, one window per worker, the queue, the
/// recent events and the settings dialogs. It is drawn from a <see cref="PageSnapshot"/> alone, so
/// the page thread never asks the scheduler anything (PAGE-1). What it looks like is the theme's
/// business (PAGE-11); what it says is the snapshot's.
/// </summary>
/// <param name="maxWorkers">
/// The Worker types panel's total (WT-P3), from the pool's <c>maxWorkers</c> (WT-C1). Host passes
/// the scheduler's own; without it the page uses the number of slots the snapshot carries.
/// </param>
/// <param name="agents">
/// The agent names the panel offers (WT-P3): scheduler.json's <c>agents</c> keys (CFG-10) plus the
/// default. Host passes them; without it the page offers the default and the agents the types use.
/// </param>
public sealed class PageRenderer(
    PageThemes? themes = null,
    PageNotice? notice = null,
    Func<int>? maxWorkers = null,
    Func<IReadOnlyList<string>>? agents = null) : IPageRenderer
{
    private const string RemoteReason = "(Only on the PC running the scheduler.)";

    private PageThemes Themes { get; } = themes ?? PageThemes.BuiltIn;

    private Func<int>? MaxWorkers { get; } = maxWorkers;

    private Func<IReadOnlyList<string>>? Agents { get; } = agents;

    /// <inheritdoc />
    public string Render(PageSnapshot snapshot, bool remote, bool controlsAllowed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'>")
          .Append("<meta name='viewport' content='width=device-width, initial-scale=1'>")
          .Append(Scheduler.Web.Server.PageFavicon.Link)
          .Append("<title>").Append(Enc(snapshot.Project)).Append(" scheduler</title>")
          .Append("<meta name='page-version' content='").Append(PageVersion.Of(snapshot)).Append("'>")
          .Append("<meta name='build-version' content='").Append(Enc(snapshot.Version)).Append("'>")
          .Append(Themes.Head())
          .Append("</head><body>");

        // 520-A (the developer, 2026-11-18): there is no top bar. Everything it carried - the
        // scheduler's own line, the theme selector, the last update and the not-responding state -
        // is in the Control window's title bar (AppendControl), inside #desk, where the page
        // script's whole-page refresh replaces it.
        // 500-C: the desk's columns. Below 1400 px they stack, in today's order; wider, the
        // common style lays them side by side (PageThemes.CommonCss).
        sb.Append("<div class='desk' id='desk'><div class='col' id='col-top'>");
        AppendNotice(sb, snapshot);
        AppendBanner(sb, snapshot);
        sb.Append("</div><div class='col' id='col-main'>");
        AppendControl(sb, snapshot, controlsAllowed);
        AppendWaiting(sb, snapshot, controlsAllowed);
        AppendRequests(sb, snapshot);
        AppendSlots(sb, snapshot, controlsAllowed);
        sb.Append("</div><div class='col' id='col-side'><div class='col' id='col-queue'>");
        AppendQueue(sb, snapshot);
        sb.Append("</div><div class='col' id='col-events'>");
        AppendEvents(sb, snapshot, controlsAllowed);
        AppendTrends(sb, snapshot);
        sb.Append("</div></div></div>");
        // WT-P3/P4: the per-worker settings dialogs are gone; the Worker types panel that replaces
        // them is part of the Control window (AppendControl).
        sb.Append(PageScript.Html).Append("</body></html>");
        return sb.ToString();
    }

    // The notice at the top of the page (MOVE-11).
    private void AppendNotice(StringBuilder sb, PageSnapshot snapshot)
    {
        if (notice?.Html() is { Length: > 0 } live)
        {
            sb.Append(live);
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.Notice))
        {
            sb.Append("<div class='notice'>").Append(Enc(snapshot.Notice)).Append("</div>");
        }
    }

    // The red health banner (HLTH-6): the theme's words, the red commit, and a way to its log.
    private void AppendBanner(StringBuilder sb, PageSnapshot snapshot)
    {
        var health = snapshot.Health;
        if (health.Green)
        {
            return;
        }

        // The snapshot's own banner text wins; without it the page uses the project theme's
        // banner words (PAGE-11). The page fills the theme's {sha} token with the sha the last
        // health check finished on (HLTH-6); before one has, Sha is "" and the token goes away.
        var banner = (!string.IsNullOrEmpty(health.Banner)
            ? health.Banner
            : Themes.RedHealthText + "\n" + Themes.RedHealthDetail)
            .Replace("{sha}", health.Sha, StringComparison.Ordinal);

        sb.Append("<div class='health-red'>").Append(Enc(banner).Replace("\n", "<br>", StringComparison.Ordinal))
          .Append(" &nbsp; <a href='/health'>show the log</a></div>");
    }

    // The Control window: state, health, server-down, category overlaps, stats, the planner line,
    // and the buttons that start and stop the scheduler (PAGE-5).
    private void AppendControl(StringBuilder sb, PageSnapshot snapshot, bool controlsAllowed)
    {
        var (text, tone) = StateOf(snapshot.State);
        var body = new StringBuilder();

        // 610-C item 5 (the developer, 2026-11-18): the body's "State: ... Health: ..." line is gone
        // - 520-A put the state in the title bar, and the health joined it there. What the line
        // carried besides the two stays: the model servers that do not answer (510-B).
        if (snapshot.Health.DownServers.Count > 0 || snapshot.Health.ServerDown)
        {
            body.Append("<div>");
            foreach (var server in snapshot.Health.DownServers)
            {
                body.Append(" &nbsp; <span class='bad'>model server ").Append(Enc(server)).Append(" does not answer</span>");
            }

            if (snapshot.Health.DownServers.Count == 0)
            {
                body.Append(" &nbsp; <span class='bad'>model server does not answer</span>");
            }

            body.Append("</div>");
        }
        foreach (var overlap in snapshot.Health.CategoryOverlaps)
        {
            body.Append("<div class='warn'>Category overlap: ").Append(Enc(overlap)).Append("</div>");
        }

        if (!string.IsNullOrEmpty(snapshot.Stats.Text))
        {
            // 520-A: the overall line and each model's line on a line of their own. The text
            // separates them with \n, which HTML-encoding turns into one run-on paragraph, so each
            // line gets its own div; pre-wrap is what keeps a model line's two-space indent.
            foreach (var line in snapshot.Stats.Text.Split('\n'))
            {
                var row = line.TrimEnd('\r');
                if (row.Trim().Length == 0)
                {
                    continue;
                }

                body.Append("<div class='stats' style='white-space: pre-wrap'>").Append(Enc(row)).Append("</div>");
            }
        }

        var tools = new StringBuilder();

        // 930-C (the developer, 2026-11-19): while a health check runs the button says so - pressed
        // like Pause and Planner push are, "Checking <sha>...", and disabled so it cannot be pressed
        // twice. The same state goes out on /live.json (LiveFeed), so the script keeps the button
        // current between refreshes and shows the press at once on a click.
        var checking = HealthChecking.Sha(snapshot.Health);
        tools.Append(checking.Length > 0
            ? Button("/health-now", $"Checking {checking}...", "A health check is running; it cannot be started again until it finishes.", controlsAllowed, "pressed", "btn-health", disabled: true)
            : Button("/health-now", "Check main", "Build main and run its gate now. A red result pauses dispatching.", controlsAllowed, id: "btn-health"));

        // Planner push and Pause are toggles that show their state (the developer, 2026-11-18): on
        // or paused, the button looks pressed; a click turns it the other way.
        var push = snapshot.PlannerPush
            ?? (string.IsNullOrEmpty(snapshot.PlannerLine) ? null : snapshot.PlannerLine.Contains("push: on", StringComparison.OrdinalIgnoreCase));
        if (push is { } on)
        {
            tools.Append(on
                ? Button("/planner-toggle", "Planner push: on", "The planner's outbox is on. Click to turn it off.", controlsAllowed, "pressed ok")
                : Button("/planner-toggle", "Planner push: off", "The planner's outbox is off. Click to turn it on.", controlsAllowed));
        }

        tools.Append(snapshot.State.State switch
        {
            SchedulerState.Running => Button("/pause", "Pause", "Start no new cards. Workers already running carry on.", controlsAllowed),
            SchedulerState.Paused or SchedulerState.PausedRed or SchedulerState.PausedQuota =>
                Button("/resume", "Paused", "Paused: no new cards start. Click to resume taking cards.", controlsAllowed, "pressed warn"),
            _ => Button("/resume", "Resume", "Start taking cards again.", controlsAllowed),
        });
        tools.Append(Button("/drain", "Drain and exit", "Start nothing new; running cards finish, then the scheduler exits.", controlsAllowed, "bad"));
        tools.Append(Button("/stopall", "Stop all and exit", "Ask every worker to stop cleanly, then exit.", controlsAllowed, "bad"));
        body.Append("<div style='display: flex; flex-wrap: wrap; gap: 8px; margin-top: 6px'>").Append(tools).Append("</div>");

        // WT-P3: the Worker types panel sits under the scheduler's own buttons, in this window.
        AppendWorkerTypes(body, snapshot, controlsAllowed);

        // 992-B: the Categories panel, under Worker types; it replaced the Lanes button.
        body.Append(CategoriesPanel.Html(snapshot, controlsAllowed));

        // The Control window's title is the scheduler's own line (the developer, 2026-11-18, 520-A,
        // and 610-C item 5): "ProjectToolkit Scheduler v<version> - running, main 1a2b3c4 green at 22:41
        //   4 workers   CPU 15%, 12G free of 31G, 32 threads". The state word and the health each
        // keep their colour, the health links its log (510-B), and the live refresh keeps both
        // current - which is why the line is built as the head's extra rather than passed as the
        // title, which is encoded. The theme selector, the last update and 470-C's not-responding
        // state - the rest of what the top bar held - sit on its right.
        var title = new StringBuilder();
        title.Append(" <span class='").Append(tone).Append("'>").Append(Enc(text)).Append("</span>");
        if (!string.IsNullOrEmpty(snapshot.Health.Text))
        {
            title.Append(", <span class='").Append(HealthTone(snapshot.Health)).Append("'>")
                 .Append(Enc(snapshot.Health.Text)).Append("</span>")
                 .Append(snapshot.Health.HasLog ? " &nbsp; <a href='/health'>log</a>" : "");
        }

        // How many of the pool's places are taken (WT-P1): the running workers, of the total.
        var workers = snapshot.Slots.Count(slot => !string.IsNullOrEmpty(slot.Card));
        title.Append(" &nbsp; ").Append(workers).Append(" of ").Append(TotalWorkers(snapshot)).Append(" workers");
        if (snapshot.HostLoad is { } load)
        {
            title.Append(" &nbsp; ").Append(Enc(HostLoadText(load)));
        }

        var bar = new StringBuilder()
            .Append(Themes.Selector())
            .Append("<span class='updated'>last update ").Append(snapshot.Built.ToString("HH:mm:ss")).Append("</span>")
            .Append(" <span id='live-state'></span>");

        // The Control window leads the main column in every layout (520-A item 5, the developer,
        // 2026-11-18). The common style promotes #w-waiting to its top (PageThemes.CommonCss);
        // PageThemes.cs is category C's, so the order is set here on the window this renderer draws
        // rather than there.
        Window(sb, $"{snapshot.Project} Scheduler v{snapshot.Version} -", body.ToString(), bar.ToString(), "id='w-control' style='order: -2'", headExtra: title.ToString());
    }

    // What is waiting on the developer: held categories, stuck cards, questions, cards waiting, and
    // review cards - a review card is hidden while its own worker still runs (PAGE-5).
    private void AppendWaiting(StringBuilder sb, PageSnapshot snapshot, bool controlsAllowed)
    {
        var running = RunningCards(snapshot);
        var items = snapshot.Waiting
            .Where(item => item.Kind != PageWaitingKind.Review || !Running(running, item.Name))
            .ToList();

        if (items.Count == 0)
        {
            // 520-A item 5 (the developer, 2026-11-18): an empty Waiting on you is no window at all.
            // The live refresh replaces the whole desk when the page version changes, so the window
            // comes back by itself when something arrives.
            return;
        }

        var table = new StringBuilder("<table class='waiting'>");
        table.Append("<tr><th></th><th>Card</th><th></th><th>Actions</th><th>Notes</th></tr>");

        foreach (var item in items)
        {
            var (word, tone) = item.Kind switch
            {
                PageWaitingKind.HeldCategory => ("held category", "bad"),
                PageWaitingKind.Stuck => ("stuck", "bad"),
                // 410-A: the run finished but never committed a Result, so the row reads as stuck,
                // not as a review card waiting to be read; its actions still lead with Done.
                PageWaitingKind.FinishedWithoutUpdate => ("stuck", "bad"),
                PageWaitingKind.Question => ("question", "bad"),
                PageWaitingKind.Waiting => ("waiting", "warn"),
                PageWaitingKind.TemplateEdited => ("template", "warn"),
                _ => ("review", "warn"),
            };

            // A held category and an edited template copy are not cards: no link, no Open (a category's Open
            // is categories.json, among its actions; a copy's are Overwrite and Keep).
            var category = item.Kind is PageWaitingKind.HeldCategory or PageWaitingKind.TemplateEdited;
            // 510-B: a reason guessed from the card's Result journal keeps its "Result:" marker, muted.
            var notes = string.IsNullOrEmpty(item.Note)
                ? "<span class='mute'>No reason given; see the card&#39;s Result.</span>"
                : item.NoteFrom == "result" && item.Note.StartsWith("Result: ", StringComparison.Ordinal)
                    ? "<span class='mute'>Result:</span> " + Enc(item.Note["Result: ".Length..])
                    : Enc(item.Note);
            var name = item.Kind switch
            {
                PageWaitingKind.HeldCategory => "Category " + Enc(item.Name),
                PageWaitingKind.TemplateEdited => Enc(item.Name),
                _ => CardLink(item.Name, item.Title),
            };
            table.Append(category ? "<tr>" : $"<tr data-card='{Enc(CardId(item.Name))}'>")
                 // 968-C: the status word, Open and the actions are short; the card ellipsises; the notes wrap.
                 .Append("<td class='nw'><span class='").Append(tone).Append("'>").Append(word).Append("</span></td><td class='card'>")
                 .Append(name).Append("</td><td class='nw'>")
                 .Append(category ? "" : Button($"/open?card={Url(item.Name)}", "Open", $"Open {item.Name} in the default editor on the PC running the scheduler.", controlsAllowed))
                 .Append("</td><td class='nw'>").Append(WaitingActions(item, snapshot.Project, controlsAllowed))
                 .Append(category ? "" : NoteBox(item.Name, controlsAllowed)).Append("</td><td class='wrap'>")
                 .Append(notes).Append("</td></tr>");

            foreach (var question in item.Questions)
            {
                table.Append(QuestionRow(item.Name, question, controlsAllowed));
            }
        }

        Window(sb, "Waiting on you", table.Append("</table>").ToString(), "", "id='w-waiting'");
    }

    // One open question on a card, as the old page drew it: the choices in the actions column, the
    // question and an optional note in the notes column. The buttons reach their form through its id.
    private static string QuestionRow(string card, CardQuestion question, bool controlsAllowed)
    {
        var dis = controlsAllowed ? "" : " disabled";
        var id = NoteId(card);
        var choices = new StringBuilder();
        for (var i = 0; i < question.Choices.Count; i++)
        {
            var choice = question.Choices[i];
            var tip = choice.Destination is { } to
                ? $"{choice.Label}; the card then goes to {to.ToString().ToLowerInvariant()}/"
                : choice.Label;
            choices.Append("<button form='").Append(id).Append("' formaction='/answer?q=").Append(question.Number).Append("' name='a' value='").Append(i)
                   .Append("' title='").Append(Enc(tip)).Append('\'').Append(dis).Append('>')
                   .Append(Enc(choice.Label)).Append("</button> ");
        }

        // 968-C: the question spans the row and its choices sit under it as one row of buttons, which
        // wraps only when the window is too narrow - not stacked in the narrow Actions column.
        return $"<tr class='qrow'><td></td><td colspan='4' class='wrap'><span class='q'>"
             + $"<input type='hidden' name='card' value='{Enc(card)}'><input type='hidden' name='q' value='{question.Number}'>"
             + $"Q{question.Number}: {Enc(question.Text)}</span>"
             + $"<div class='qchoices'>{choices.ToString().TrimEnd()}</div></td></tr>";
    }

    private static string WaitingActions(PageWaitingItem item, string project, bool controlsAllowed) => item.Kind switch
    {
        PageWaitingKind.HeldCategory =>
            Button("/open-lanes", "Open", "Open the categories file in the editor.", controlsAllowed)
            + Button($"/accept-lane?lane={Url(item.Name)}", "Accept", "This category's cards still fit its new definition; dispatch it again.", controlsAllowed),
        // Roadmap item 1 (580-A): Overwrite takes the toolkit's template; Keep leaves the project's
        // copy and stops asking until the toolkit's template changes again.
        PageWaitingKind.TemplateEdited =>
            Button($"/template-overwrite?project={Url(project)}&file={Url(item.Name)}", "Overwrite", "Take the toolkit's template for this file, in one commit to main.", controlsAllowed)
            + Button($"/template-keep?project={Url(project)}&file={Url(item.Name)}", "Keep", "Leave the project's copy and stop asking until the toolkit's template changes again.", controlsAllowed),
        // 946-B: the log of the run that stuck it, beside the button that clears the count.
        PageWaitingKind.Stuck =>
            NoteButton(NoteId(item.Name), "/retry", "Retry", "Clear the run count so the scheduler resumes it. The note, if any, goes into the card's ## Dev section.", controlsAllowed)
            + LogLink(item.Log, "The last run&#39;s log"),
        PageWaitingKind.Question =>
            NoteButton(NoteId(item.Name), "/resume-card", "Resume", "Back to work: set it ready so a worker resumes it now. The note, if any, goes into the card's ## Dev section.", controlsAllowed),
        PageWaitingKind.Waiting =>
            NoteButton(NoteId(item.Name), "/resume-card", "Ready", "Done what it waits for: set it ready. The note, if any, goes into the card's ## Dev section.", controlsAllowed),
        _ =>
            $"<a class='g' href='/review?card={Url(item.Name)}' title='Open the review panel: commits, files, Result.'>Review</a> "
            + NoteButton(NoteId(item.Name), "/done", "Done", "Reviewed: mark this card done and move it to done/. The note, if any, goes into the card's ## Dev section.", controlsAllowed)
            + NoteButton(NoteId(item.Name), "/resume-card", "Send back", "Not finished: back to active/, ready. The note, if any, goes into the card's ## Dev section.", controlsAllowed),
    };

    // 1099-C: a card's one note box with its Save note button; the card's other buttons (and its question
    // answers) are buttons of this form, so what is typed goes with whichever is pressed.
    private static string NoteId(string card) =>
        System.Text.RegularExpressions.Regex.Replace($"n-{CardId(card)}", "[^A-Za-z0-9-]", "-");

    private static string NoteBox(string card, bool controlsAllowed) =>
        controlsAllowed
            ? $"<form id='{NoteId(card)}' method='post' action='/note' class='g' data-tip='Type a note, then press any button of this card to send it with that, or Save note to file it in the card&#39;s ## Dev section without moving it.'>"
              + $"<input type='hidden' name='card' value='{Enc(card)}'><input name='note' maxlength='500' placeholder='note (optional)'> <button>Save note</button></form>"
            : "";

    private static string NoteButton(string id, string action, string label, string tip, bool controlsAllowed) =>
        controlsAllowed
            ? $"<span class='g' data-tip='{Enc(tip)}'><button form='{id}' formaction='{action}' aria-label='{Enc(label + ". " + tip)}'>{Enc(label)}</button></span>"
            : $"<span class='g' data-tip='{Enc(tip + " " + RemoteReason)}'><button disabled>{Enc(label)}</button></span>";

    // Requests to the planner: the open requests, and the cards it has to rewrite or release.
    private void AppendRequests(StringBuilder sb, PageSnapshot snapshot)
    {
        var open = snapshot.Planner.OpenRequests.Where(request => request.Open).ToList();
        if (open.Count == 0 && snapshot.Planner.WaitingCards.Count == 0)
        {
            // 520-A item 5 (the developer, 2026-11-18): no window at all when it holds nothing.
            return;
        }

        var table = new StringBuilder("<table>");
        if (open.Count == 0)
        {
            table.Append("<tr><td class='mute'>None open.</td></tr>");
        }
        else
        {
            table.Append("<tr><th>Request</th><th>For</th></tr>");
            foreach (var request in open)
            {
                table.Append("<tr><td><a href='/request?name=").Append(Url(request.Name)).Append("'>")
                     .Append(Enc(string.IsNullOrEmpty(request.Title) ? request.Name : request.Title)).Append("</a></td><td>")
                     .Append(Enc(request.For)).Append("</td></tr>");
            }
        }

        table.Append("</table>");

        if (snapshot.Planner.WaitingCards.Count > 0)
        {
            // 510-B: each card's Depends on links, and "all done" for each card whose dependencies
            // are all finished (WaitingReady), as the old page did.
            var perCard = snapshot.Planner.WaitingDepends.Count > 0 || snapshot.Planner.WaitingReady.Count > 0;
            table.Append("<table style='margin-top:8px'><tr><th>Card waiting on the planner</th><th>Depends on</th><th>For</th></tr>");
            foreach (var card in snapshot.Planner.WaitingCards)
            {
                var ready = perCard ? snapshot.Planner.WaitingReady.Contains(card, StringComparer.OrdinalIgnoreCase) : snapshot.Planner.AllDone;
                var depends = snapshot.Planner.WaitingDepends.TryGetValue(card, out var list) ? list : [];
                table.Append("<tr data-card='").Append(Enc(CardId(card))).Append("'><td>").Append(CardLink(card)).Append("</td><td>")
                     .Append(DepLinks(depends, snapshot)).Append("</td><td>")
                     .Append(ready ? "<span class='warn'>all done: the planner can act now</span>" : "<span class='mute'>waiting</span>")
                     .Append("</td></tr>");
            }

            table.Append("</table>");
        }

        Window(sb, "Requests to the planner", table.ToString(),
            $"<span class='tag'>{open.Count} open{(snapshot.Planner.WaitingCards.Count > 0 ? $", {snapshot.Planner.WaitingCards.Count} cards" : "")}</span>",
            "id='w-requests'");
    }

    // One window per running worker (PAGE-5, WT-P1): its type and card, what it has said, and the
    // ways to stop it. An idle folder has no window, and no worker has Disable, Enable or Settings
    // buttons any more - what they did belongs to the type, and the type is edited in the Worker
    // types panel (AppendWorkerTypes).
    private void AppendSlots(StringBuilder sb, PageSnapshot snapshot, bool controlsAllowed)
    {
        sb.Append("<div class='slots' id='w-slots'>");
        var running = snapshot.Slots.Where(slot => !string.IsNullOrEmpty(slot.Card)).ToList();
        if (running.Count == 0)
        {
            // WT-P1: with none running, one line says how many places there are to run in.
            sb.Append("<div class='mute'>No workers running (0 of ").Append(TotalWorkers(snapshot)).Append(")</div>");
            sb.Append("</div>");
            return;
        }

        foreach (var slot in running)
        {
            var s = slot.Slot;
            var body = new StringBuilder();
            if (!string.IsNullOrEmpty(slot.StopWhy))
            {
                body.Append("<div class='warn'>Stopping: ").Append(Enc(slot.StopWhy))
                    .Append(slot.KillAt is { } killAt ? $". Killed at {killAt:HH:mm} if still running." : "")
                    .Append("</div>");
            }

            // Always there, so the live view has a place to put the stats line (PAGE-3, PAGE-10).
            // The model and the model server's address come off the end of the stats line (490-C,
            // 971-C): the title bar already names the model, and the address is only shown where it
            // is set, in the Worker types panel. The model stays on the div as data-model, so the
            // live view trims the line the same way the server side does.
            body.Append("<div class='stats' data-slot='").Append(s).Append("' data-model='")
                .Append(Enc(slot.Model)).Append("'>").Append(Enc(StatsLine(slot.Stats, slot.Model))).Append("</div>");

            // WT-P1's title: "<type> - slot <n>", then the model, and the card link the head carries
            // after it completes the line the design asks for.
            var model = string.IsNullOrEmpty(slot.ModelShort) ? slot.Model : slot.ModelShort;
            var title = $"{TypeName(slot, snapshot)} - slot {s}"
                + (string.IsNullOrEmpty(model) ? "" : $" ({model})");
            string bar;
            string headExtra;

            body.Append("<pre class='log' data-slot='").Append(s).Append("'>")
                .Append(Enc(string.Join('\n', slot.LogTail))).Append("</pre>");
            body.Append("<form method='post' action='/send?slot=").Append(s).Append("' class='send'><input name='text' maxlength='2000' placeholder='Message to this worker'")
                .Append(controlsAllowed ? "" : " disabled").Append("><button").Append(controlsAllowed ? "" : " disabled").Append(">Send</button>")
                // 510-B: a message the worker has not read yet, as the old page said it.
                .Append(slot.MessagePending ? " <span class='warn'>waiting for the worker to read it</span>" : "")
                // 700-B: and when the hook last gave it one, off the read file's stamp.
                .Append(slot.MessageReadAt is { } readAt ? " <span class='mute'>read at " + $"{readAt:HH:mm:ss}</span>" : "")
                .Append("</form>");

            // The title bar, every size (mini included): the time-limit bar, the spinner with
            // the average T/s (shown when minimised), and the minutes run. The card sits in
            // the title itself, right after the worker's name (490-C).
            // 510-B: minutes run out of the time limit, so the bar is filled from the first draw.
            var run = slot.Start is { } start ? Math.Max(0, (int)(snapshot.Built - start).TotalMinutes) : (int?)null;
            // A quiet worker (no output for 5 minutes or more) says so, and its spinner stops.
            var minutes = (run is null ? "" : slot.TimeLimit is { } limit and > 0 ? $"{run}/{limit} min" : $"{run} min")
                + (slot.QuietMinutes is { } quiet ? $", quiet {quiet} min" : "");
            var fill = run is { } r && slot.TimeLimit is { } l && l > 0 ? Math.Min(100, 100 * r / l) : (int?)null;
            bar = (fill is { } pct ? $"<span class='prog{(pct >= 90 ? " late" : "")}' data-slot='{s}' style='width:{pct}%'></span>" : $"<span class='prog' data-slot='{s}'></span>")
                + $"<span class='mini{(slot.QuietMinutes is null ? "" : " idle")}' data-slot='{s}'><span class='spin'></span><span class='tps'>{Enc(TpsText(slot.Stats))}</span></span>"
                + $"<span class='tag' data-slot='{s}'>{minutes}</span>";
            // 510-B: the card link's tooltip is its title and its category's name (the category text left the title in 490-C).
            var tip = slot.CardTitle + (string.IsNullOrEmpty(slot.CategoryName) ? "" : $"{(slot.CardTitle.Length > 0 ? " - " : "")}category {CategoryOf(slot.Card)} ({slot.CategoryName})");
            headExtra = CardLink(slot.Card, tip, cls: "cardlink");

            // WT-P1's buttons, in the developer's order: Minimize/Expand, Stop, Kill. There is no
            // slot left to disable (WT-P2): the card waits under Waiting on you with Resume.
            var tools = new StringBuilder();
            tools.Append($"<button type='button' class='logtoggle' data-slot='{s}' title='Show only the title bar'>Minimize</button>");
            if (string.IsNullOrEmpty(slot.StopWhy))
            {
                tools.Append(Button($"/stop?slot={s}", "Stop", "Ask this worker to stop at its next safe point: push green work, note where to resume, and leave the card waiting under Waiting on you with Resume.", controlsAllowed));
            }

            tools.Append(Button($"/kill?slot={s}", "Kill", "End this worker and everything it started, now. Unpushed work is lost; the card waits under Waiting on you with Resume.", controlsAllowed, "bad"));

            Window(sb, title, body.ToString(), bar + tools, $"data-card='{Enc(CardId(slot.Card))}'", $"/log?slot={s}", headExtra);
        }

        sb.Append("</div>");
    }

    // The queue, in the order it will run (PAGE-5, order by PAGE-6).
    private void AppendQueue(StringBuilder sb, PageSnapshot snapshot)
    {
        // 510-B: the old page's columns, with the newest run's log and the Depends on links.
        var table = new StringBuilder("<table><tr><th>#</th><th>Card</th><th>Log</th><th>Category</th><th>Depends on</th><th>State</th></tr>");
        var count = 0;
        foreach (var item in snapshot.Queue)
        {
            count++;
            var tone = item.Note.StartsWith("next up", StringComparison.OrdinalIgnoreCase) ? "ok"
                : item.Note.Contains("needs you", StringComparison.OrdinalIgnoreCase) ? "bad"
                : "mute";
            // 968-C: every column but the note is short and never broken; the note is the one that wraps.
            table.Append("<tr data-card='").Append(Enc(CardId(item.Name))).Append("'><td class='mute nw'>").Append(count)
                 .Append("</td><td class='card'>").Append(CardLink(item.Name, CardTip(item.Name, item.Title)))
                 .Append("</td><td class='nw'>").Append(LogLink(item.Log, "The newest run&#39;s log"))
                 .Append("</td><td class='nw'>").Append(CategoryChip.Html(snapshot.Categories, CategoryOf(item.Name)))
                 .Append("</td><td class='nw'>").Append(DepLinks(item.Depends, snapshot))
                 .Append("</td><td class='").Append(tone).Append(" wrap'>")
                 .Append(Enc(item.Note));
            // Roadmap item 4 (640-A): the time-box warning or suggestion rides in the state column.
            if (item.TimeBoxNote.Length > 0)
            {
                table.Append(" <span class='warn'>").Append(Enc(item.TimeBoxNote)).Append("</span>");
            }

            table.Append("</td></tr>");
        }

        if (count == 0)
        {
            table.Append("<tr><td></td><td class='mute'>Empty.</td></tr>");
        }

        Window(sb, "Queue", table.Append("</table>").ToString(), $"<span class='tag'>{count} cards</span>", "id='w-queue'");
    }

    // 950-C (PAGE-5, WT-P6): the Worker column says who did the work - the worker type's name as
    // workers.json names it. The rows written before accounting gained the type column have none,
    // so they fall back to the model, and to the slot number this column showed before.
    private static string WorkerColumn(PageEvent e) =>
        !string.IsNullOrEmpty(e.Type) ? e.Type
        : !string.IsNullOrEmpty(e.Model) ? e.Model
        : e.Slot?.ToString() ?? "";

    // Recent events: the last 10 finished runs (PAGE-5).
    // 510-B: as the old page drew it: worker, card, result, minutes, the run's log and Open. An
    // event that is not a run (no card) keeps its line of text.
    private void AppendEvents(StringBuilder sb, PageSnapshot snapshot, bool controlsAllowed)
    {
        var table = new StringBuilder("<table><tr><th>When</th><th>Worker</th><th>Card</th><th>Result</th><th>Time</th><th></th><th></th></tr>");
        var shown = 0;
        foreach (var e in snapshot.Events.Take(10))
        {
            shown++;
            var when = e.Time.ToString("MM-dd HH:mm");
            if (string.IsNullOrEmpty(e.Card))
            {
                // An event that is not a run is a line of text: it is the wrapping kind of column.
                table.Append("<tr><td class='nw'>").Append(when).Append("</td><td class='wrap' colspan='6'><span class='")
                     .Append(Tone(e.Tone)).Append("'>").Append(Enc(e.Text)).Append("</span></td></tr>");
                continue;
            }

            // 968-C: When, Worker, Result, Time, the log and Open are short; the card ellipsises, with
            // the whole name in its tooltip.
            var id = CardId(e.Card);
            table.Append("<tr data-card='").Append(Enc(id)).Append("'><td class='nw'>").Append(when)
                 .Append("</td><td class='nw'>").Append(Enc(WorkerColumn(e)))
                 .Append("</td><td class='card'>").Append(CardLink(e.Card, e.Card))
                 .Append("</td><td class='nw'><span class='").Append(Tone(e.Tone)).Append("'>").Append(Enc(e.Result)).Append("</span>")
                 .Append("</td><td class='nw'>").Append(e.Minutes is { } m ? $"{m} min" : "")
                 .Append("</td><td class='nw'>").Append(LogLink(e.Log, "This run&#39;s log"))
                 .Append("</td><td class='nw'>").Append(Button($"/open?card={Url(e.Card)}", "Open", $"Open {e.Card} in the default editor on the PC running the scheduler.", controlsAllowed))
                 .Append("</td></tr>");
        }

        if (shown == 0)
        {
            table.Append("<tr><td class='mute'>None yet.</td></tr>");
        }

        Window(sb, "Recent events", table.Append("</table>").ToString(), "", "id='w-events'");
    }

    // The Trends window (roadmap item 3, card 630-A): the cards finished each day for the last 14
    // days, by model, under Recent events in the side column. Hidden while the accounting holds
    // less than one day of data - one day of bars is not a trend.
    private void AppendTrends(StringBuilder sb, PageSnapshot snapshot)
    {
        if (!Trends.HasTrend(snapshot.Trends))
        {
            return;
        }

        Window(
            sb,
            "Trends",
            TrendChart.Html(snapshot.Trends, snapshot.WorkerTypes),
            "<span class='mute'>cards finished, last " + Trends.WindowDays + " days</span>",
            "id='w-trends'");
    }

    // The Worker types panel (WT-P3), in the Control window: the total, one row per type in config
    // order, an Add type button, Remove on a type with none running, and Save, which posts the
    // whole panel to /worker-types (WT-P4). It is what the per-worker settings dialogs became: a
    // worker's server, model and areas belong to its type, not to the folder it runs in.
    private void AppendWorkerTypes(StringBuilder sb, PageSnapshot snapshot, bool controlsAllowed) =>
        sb.Append(WorkerTypesPanel(snapshot, controlsAllowed, null, TotalWorkers(snapshot), Agents?.Invoke()));

    /// <summary>
    /// The panel on its own, so the combined page (which has no Control window) can carry one per
    /// project: WT-P3. A project names the pool member whose config Save writes (WT-C8); empty is
    /// the single project's own workers.json (WT-C7).
    /// </summary>
    internal static string WorkerTypesPanel(
        PageSnapshot snapshot,
        bool controlsAllowed,
        string? project,
        int total,
        IReadOnlyList<string>? agentNames)
    {
        var dis = controlsAllowed ? "" : " disabled";
        var types = snapshot.WorkerTypes;
        var pool = types.Any(type => type.Projects.Count > 0);
        var agents = AgentChoices(types, agentNames);
        var at = string.IsNullOrEmpty(project) ? "" : "?project=" + Url(project);
        var key = string.IsNullOrEmpty(project) ? "" : "-" + Url(project);

        var table = new StringBuilder()
            .Append("<tr><th>Type</th><th>Running</th><th>Cap</th><th>Agent</th><th>Model</th><th>Endpoint</th><th>Metrics</th><th>Default</th>")
            .Append(pool ? "<th>Projects</th>" : "")
            .Append("<th></th></tr>");
        for (var n = 0; n < types.Count; n++)
        {
            table.Append(TypeRow(types[n], n, dis, pool, agents, key));
        }

        var sb = new StringBuilder()
            .Append("<form method='post' action='/worker-types").Append(at).Append("' class='wtypes wthidden'")
            .Append(string.IsNullOrEmpty(project) ? "" : " data-project='" + Enc(project) + "'").Append(">")
          .Append("<div class='wtypesbar'><button type='button' class='wttoggle' data-key='wtypes-open").Append(key).Append("'")
          .Append(" title='Show or hide the Worker types settings; remembered in this browser'>Worker types <span class='wtarrow'>&#9656;</span></button> ")
          .Append(string.IsNullOrEmpty(project) ? "" : "<span class='proj'>" + Enc(project) + "</span> ")
          .Append("<span class='mute wtsum'>").Append(Enc(TypesSummary(types, total))).Append("</span></div>")
          .Append("<div class='wtbody'><div class='wtypesbar'>")
          .Append("<label>Max active workers <input name='maxWorkers' value='").Append(total)
          .Append("' size='3' title='the most workers that run at once, across all types; each type&#39;s cap is within this'").Append(dis)
          .Append("></label> <button type='button' class='addtype'").Append(dis)
          .Append(" data-tpl='wtype-new").Append(key).Append("'")
          .Append(" title='Add a type to the end of the list'>Add type</button> <button").Append(dis)
          .Append(" title='Write these types to workers.json; the change is committed and applies to the next tick'>Save</button>")
          .Append(" <span class='mute'>Cap 0 turns a type off; the caps share the max.</span></div>")
          .Append("<table class='wtypes'>").Append(table).Append("</table></div>")
          // The row Add type copies. A template's contents are inert, so its fields post nothing.
          .Append("<template id='wtype-new").Append(key).Append("'>")
          .Append(TypeRow(BlankType, 0, dis, pool, agents, key)).Append("</template>")
          .Append("</form>");

        // WT-9: an out-of-quota agent, with the time and a Resume. Each Resume is a form of its own
        // that the row's button points at, since a form cannot hold the panel's Save form.
        foreach (var (type, n) in types.Select((t, i) => (t, i)).Where(pair => pair.t.OutOfQuotaSince is not null))
        {
            sb.Append("<form id='wresume").Append(key).Append('-').Append(n).Append("' method='post' action='/agent-resume?agent=")
              .Append(Url(type.Agent)).Append(string.IsNullOrEmpty(project) ? "" : "&project=" + Url(project)).Append("'></form>");
        }

        return sb.ToString();
    }

    // The heading's one line while the panel is hidden: "Spark 1 of 4, Sonnet 0 of 4 - max 4 active".
    private static string TypesSummary(IReadOnlyList<PageWorkerTypeView> types, int total) =>
        string.Join(", ", types.Select(type => $"{type.Name} {type.Running} of {type.Max}"))
        + (types.Count > 0 ? " - " : "") + $"max {total} active";

    // One row of the panel: name, "running of max", cap, agent, model, endpoint, metrics, default (1147-C: a radio),
    // areas, and projects in a pool. Every field posts a value so
    // the route can read the rows as lists of the same length.
    private static string TypeRow(PageWorkerTypeView type, int n, string dis, bool pool, IReadOnlyList<string> agents, string key)
    {
        var row = new StringBuilder()
            .Append("<tr class='wtyperow'>")
            // 947-B: the name as loaded travels with the row, so the Save can tell this row being
            // renamed from one row removed and another added. The Add type template's row has no name
            // at all, which is how a new type says it is new.
            .Append("<td><input type='hidden' name='worig' value='")
            .Append(Enc(type.OriginalName.Length > 0 ? type.OriginalName : type.Name)).Append("'>")
            .Append("<input name='wname' value='").Append(Enc(type.Name)).Append("' size='10'").Append(dis).Append("></td>")
            .Append("<td class='mute'><span class='wrunning'>").Append(type.Running).Append(" of ").Append(type.Max).Append("</span></td>")
            .Append("<td><input name='wmax' value='").Append(type.Max).Append("' size='3' title='How many of this type may run at once; 0 is off'").Append(dis).Append("></td>")
            .Append("<td>").Append(AgentSelect(type.Agent, dis, agents));

        if (type.OutOfQuotaSince is { } since)
        {
            row.Append(" <span class='warn'>out of quota since ").Append(since.ToString("HH:mm"))
               .Append("</span> <button type='submit' form='wresume").Append(key).Append('-').Append(n)
               .Append("' title='Let this agent start workers again'>Resume</button>");
        }

        row.Append("</td>")
            // 1087-C (WT-P3): a type with no model of its own shows the model it really runs - the
            // agent's default, or the name a server alias serves - greyed as the box's placeholder.
            // The value stays empty, so a Save of an untouched row still writes no model.
            .Append("<td><input name='wmodel' value='").Append(Enc(type.Model)).Append("' size='14'")
            .Append(type.Model.Length == 0 && type.EffectiveModel.Length > 0 ? " placeholder='" + Enc(type.EffectiveModel) + "'" : "")
            .Append(dis).Append("></td>")
            .Append("<td><input name='wendpoint' value='").Append(Enc(type.Endpoint)).Append("' size='18' placeholder='as in scheduler.json'").Append(dis).Append("></td>")
            .Append("<td><input name='wmetrics' value='").Append(Enc(type.MetricsUrl)).Append("' size='18' placeholder='as in scheduler.json'").Append(dis).Append("></td>")
            .Append("<td><input type='radio' name='wdefault' value='").Append(Enc(type.Name)).Append("'").Append(type.IsDefault ? " checked" : "")
            .Append(" title='Cards with no Worker: line go to this type'").Append(dis).Append("></td>");

        if (pool)
        {
            row.Append("<td><input name='wprojects' value='").Append(Enc(string.Join(",", type.Projects)))
               .Append("' size='14' placeholder='every project'").Append(dis).Append("></td>");
        }

        // WT-P3: Remove only where none of the type is running; a running worker keeps its type.
        return row.Append("<td><button type='button' class='typeremove'")
            .Append(type.Running > 0 ? " disabled title='A worker of this type is running'" : " title='Take this type out of the list'")
            .Append(">Remove</button></td></tr>")
            .ToString();
    }

    private static readonly PageWorkerTypeView BlankType = new() { Name = "" };

    // The agent choice (WT-P3): scheduler.json's agents and the default (the worker section).
    private static string AgentSelect(string agent, string dis, IReadOnlyList<string> agents)
    {
        var select = new StringBuilder().Append("<select name='wagent'").Append(dis).Append(">");
        foreach (var name in agents)
        {
            select.Append("<option value='").Append(Enc(name)).Append("'")
                  .Append(string.Equals(name, agent, StringComparison.OrdinalIgnoreCase) ? " selected" : "")
                  .Append(">").Append(name.Length == 0 ? "default" : Enc(name)).Append("</option>");
        }

        return select.Append("</select>").ToString();
    }

    // The agents the panel offers: what the host gives (CFG-10), then the ones the types already
    // use, so a row never shows a name the panel itself would refuse.
    private static IReadOnlyList<string> AgentChoices(IReadOnlyList<PageWorkerTypeView> types, IReadOnlyList<string>? agentNames)
    {
        var names = new List<string> { "" };
        foreach (var name in (agentNames ?? []).Concat(types.Select(type => type.Agent)))
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0 && !names.Any(have => string.Equals(have, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(trimmed);
            }
        }

        return names;
    }

    // The type a worker window names (WT-P1): its type's name, the worker's name where a type has
    // none, and "(removed)" when no row of the panel has it (WT-13).
    private static string TypeName(PageSlotView slot, PageSnapshot snapshot)
    {
        var name = !string.IsNullOrEmpty(slot.Type) ? slot.Type
            : !string.IsNullOrEmpty(slot.WorkerName) ? slot.WorkerName
            : "worker";
        var gone = snapshot.WorkerTypes.Count > 0
            && !snapshot.WorkerTypes.Any(type => string.Equals(type.Name, slot.Type, StringComparison.OrdinalIgnoreCase));
        return gone ? name + " (removed)" : name;
    }

    // The pool's total (WT-C1), for the panel's field and the "0 of 4" line: what the host says it
    // is, or, until it does, the number of slots the snapshot carries.
    private int TotalWorkers(PageSnapshot snapshot)
    {
        var total = MaxWorkers?.Invoke() ?? 0;
        return total > 0 ? total : Math.Max(snapshot.Slots.Count, snapshot.WorkerTypes.Count);
    }

    /// <summary>The health's colour in the Control window's title (610-C): green while main is
    /// green, red when it is red, and muted before a health check has finished on a commit.</summary>
    internal static string HealthTone(PageHealthView health) =>
        health.Sha.Length == 0 ? "mute" : health.Green ? "ok" : "bad";

    /// <summary>The state's word and colour class (PAGE-8).</summary>
    internal static (string Text, string Tone) StateOf(PageStateView state) => state.State switch
    {
        SchedulerState.Running => ("running", "ok"),
        SchedulerState.Paused => ("paused", "warn"),
        SchedulerState.PausedRed => ("paused: main is red", "bad"),
        SchedulerState.PausedQuota => ("paused: quota used up", "bad"),
        SchedulerState.Draining => ("draining", "warn"),
        _ => (state.Text, "bad"),
    };

    private static string Tone(PageTone tone) => tone switch
    {
        PageTone.Ok => "ok",
        PageTone.Warn => "warn",
        _ => "bad",
    };

    internal static string HostLoadText(HostLoadSample load) =>
        $"CPU {(int)Math.Round(load.CpuPercent)}%, {load.FreeMemoryBytes / (1024 * 1024 * 1024)}G free of {load.TotalMemoryBytes / (1024 * 1024 * 1024)}G, {load.Cores} threads";

    private static HashSet<string> RunningCards(PageSnapshot snapshot) =>
        new(
            snapshot.Slots.Where(slot => !string.IsNullOrEmpty(slot.Card)).Select(slot => CardId(slot.Card)),
            StringComparer.OrdinalIgnoreCase);

    private static bool Running(HashSet<string> running, string card) => running.Contains(CardId(card));

    /// <summary>The <c>090-C</c> part of <c>090-C-page-views.md</c>, which is how the old page matched a card.</summary>
    private static string CardId(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^\d+-[A-Za-z]");
        return match.Success ? match.Value : name;
    }

    private static string CategoryOf(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^\d+-([A-Za-z])");
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>The minimised title bar's number: the stats line's average T/s, as the old page read it.</summary>
    private static string TpsText(string stats)
    {
        var match = System.Text.RegularExpressions.Regex.Match(stats ?? "", @"([\d.]+) avg");
        return match.Success ? match.Groups[1].Value + " T/s avg" : "- T/s";
    }

    /// <summary>
    /// 968-C: a table's card cell shortens the name with an ellipsis when the row runs out of room, so
    /// its tooltip carries the whole name, after the card's title when it has one.
    /// </summary>
    private static string CardTip(string name, string title) =>
        title.Length > 0 ? $"{title} - {name}" : name;

    private static string CardLink(string name, string title = "", string cls = "") =>
        string.IsNullOrEmpty(name) ? ""
        : $"<a{(string.IsNullOrEmpty(cls) ? "" : $" class='{cls}'")} href='/card?name={Url(name)}'{(string.IsNullOrEmpty(title) ? "" : $" title='{Enc(title)}'")}>{Enc(name)}</a>";

    /// <summary>
    /// The stats line without the model and the model server's address Host appends at its end
    /// (490-C, 971-C): the title bar already names the model, and the address is shown only in the
    /// Worker types panel, where it is set. The mirror of <c>statsBits</c> in the page script, which
    /// trims the live updates the same way.
    /// </summary>
    internal static string StatsLine(string stats, string model)
    {
        var parts = stats.Split("  |  ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var end = parts.Length;
        while (end > 0)
        {
            var part = parts[end - 1];
            var isServer = part.Contains("://", StringComparison.Ordinal);
            var isModel = model.Length > 0 && string.Equals(part, model, StringComparison.Ordinal);
            if (!isServer && !isModel)
            {
                break;
            }

            end--;
        }

        return string.Join("  |  ", parts[..end]);
    }

    // 510-B: the old page's Dep-Link: a card id that, pointed at, lights up the card wherever the
    // page shows it (the page script, data-dep); titled with the card's title where the page has it.
    private static string DepLinks(IReadOnlyList<string> depends, PageSnapshot snapshot) =>
        string.Join(", ", depends.Select(id =>
        {
            var title = snapshot.Queue.FirstOrDefault(q => string.Equals(CardId(q.Name), id, StringComparison.OrdinalIgnoreCase))?.Title
                ?? snapshot.Slots.FirstOrDefault(s => !string.IsNullOrEmpty(s.Card) && string.Equals(CardId(s.Card), id, StringComparison.OrdinalIgnoreCase))?.CardTitle
                ?? "";
            return $"<span class='dep' data-dep='{Enc(id)}'{(string.IsNullOrEmpty(title) ? "" : $" title='{Enc(title)}'")}>{Enc(id)}</span>";
        }));

    // 510-B: a link to a run's log by its file name (the page server serves only its own logs).
    private static string LogLink(string log, string tip) =>
        string.IsNullOrEmpty(log) ? ""
        : $"<a href='/log?file={Url(Path.GetFileName(log))}' title='{tip}'>log</a>";

    private static string Button(string action, string label, string tip, bool controlsAllowed, string cls = "", string id = "", bool disabled = false)
    {
        var text = Enc(label);
        var idAttr = id.Length == 0 ? "" : $" id='{id}'";
        if (!controlsAllowed)
        {
            return $"<span class='g' data-tip='{Enc(tip + " " + RemoteReason)}'><button class='{cls}'{idAttr} disabled>{text}</button></span>";
        }

        // disabled: the button shows a state that cannot be pressed again (930-C) but keeps its form
        // and its tip, so the live refresh can hand it back pressable when the state ends.
        var dis = disabled ? " disabled" : "";
        return $"<form method='post' action='{action}' class='g' data-tip='{Enc(tip)}'>"
             + $"<button class='{cls}'{idAttr}{dis} aria-label='{Enc(label + ". " + tip)}'>{text}</button></form>";
    }

    private static void Window(StringBuilder sb, string title, string body, string extra, string attrs = "", string href = "", string headExtra = "")
    {
        var head = string.IsNullOrEmpty(href)
            ? Enc(title)
            : $"<a class='barlink' href='{href}'>{Enc(title)}</a>";
        sb.Append("<section class='win'")
          .Append(string.IsNullOrEmpty(attrs) ? "" : " " + attrs)
          .Append("><div class='bar'><b>").Append(head).Append(headExtra).Append("</b>").Append(extra)
          .Append("</div><div class='inner'>").Append(body).Append("</div></section>");
    }

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
