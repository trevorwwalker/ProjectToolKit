using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// The route table of PAGE-9: the GET routes that answer from a snapshot or from the host, and the
/// POST routes. A POST is validated here and then either run at once - the pure state actions, which
/// touch nothing but the in-memory state - or put on <see cref="Queue"/>, whose single worker thread
/// does the git work in click order; either way the reply goes out at once, 202 with the queued id
/// when the page script asked or 303 back to the page when a form did (PAGE-1, 957-B). A control
/// from another PC is refused with 403 unless remoteControl is on (PAGE-7).
/// </summary>
public sealed partial class PageRouter
{
    /// <summary>What a refused control says, as the old scheduler said it.</summary>
    public const string RemoteRefused =
        "The controls work only on the PC running the scheduler (set remoteControl in scheduler.json to allow them from here).";

    private const string NoSuchLog = "no such log";

    private static readonly Regex LogName = LogNamePattern();

    private static readonly Regex Runs = RunsPattern();

    /// <summary>
    /// A router over one page's data, actions and live feed.
    /// </summary>
    /// <param name="data">The snapshot and the host's texts.</param>
    /// <param name="actions">Every POST route, as the contract has them.</param>
    /// <param name="renderer">The page itself, built by the views area.</param>
    /// <param name="live">The live view of PAGE-3.</param>
    /// <param name="remoteControl">Whether another PC may use the controls.</param>
    /// <param name="queue">
    /// The one background worker the POST actions run on (957-B); a router with no queue of its own
    /// makes one, and whoever owns the router disposes it.
    /// </param>
    /// <param name="pool">
    /// A pool's data, actions and page (MP-P8), or null for one project, which ignores <c>project=</c>.
    /// In a pool, <paramref name="data"/> is the pool's and <paramref name="actions"/> answer the
    /// pool-wide routes <c>/tick</c>, <c>/worker-types</c> and <c>/agent-resume</c>.
    /// </param>
    public PageRouter(
        IPageData data,
        IPageActions actions,
        IPageRenderer renderer,
        LiveFeed live,
        bool remoteControl,
        PageActionQueue? queue = null,
        PoolRouting? pool = null)
        : this(data, actions, renderer, live, remoteControl, queue ?? new PageActionQueue(), landed: true)
    {
        Pool = pool;
    }

    // One project's router inside a pool shares the pool router's feed and queue (landed: false, the
    // pool router already set the queue's hook).
    private PageRouter(
        IPageData data,
        IPageActions actions,
        IPageRenderer renderer,
        LiveFeed live,
        bool remoteControl,
        PageActionQueue queue,
        bool landed)
    {
        Data = data;
        Actions = actions;
        Renderer = renderer;
        Live = live;
        RemoteControl = remoteControl;
        Queue = queue;

        // The page waits on its click until the scheduler's own view is newer than the click, so the
        // click is noted when its work has landed rather than when it came in (957-B).
        if (landed)
        {
            Queue.Landed = _ => Live.NoteClick();
        }
    }

    /// <summary>The worker the POST actions run on (957-B). The page server disposes it.</summary>
    public PageActionQueue Queue { get; }

    private IPageData Data { get; }

    private IPageActions Actions { get; }

    private IPageRenderer Renderer { get; }

    private LiveFeed Live { get; }

    private bool RemoteControl { get; }

    private PoolRouting? Pool { get; }

    [GeneratedRegex(@"^\d+-[A-Z]-\d{8}-\d{6}\.log$")]
    private static partial Regex LogNamePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex RunsPattern();

    /// <summary>Answers one request.</summary>
    public PageResponse Handle(PageRequest request)
    {
        var isGet = string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase);
        if (!isGet && !(request.IsLocal || RemoteControl))
        {
            return PageResponse.Forbidden(RemoteRefused);
        }

        if (Pool is not null && PoolRoute(Pool, request, isGet) is { } routed)
        {
            return routed;
        }

        return isGet
            ? Get(request)
            : string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
                ? Post(request)
                : PageResponse.MethodNotAllowed();
    }

    private PageResponse Get(PageRequest request)
    {
        switch (request.Path)
        {
            case "/":
                return PageResponse.Html(Renderer.Render(
                    Data.Snapshot(),
                    remote: !request.IsLocal,
                    controlsAllowed: request.IsLocal || RemoteControl));

            case "/favicon.ico":
                return PageFavicon.Response();

            case "/font":
                return Font();

            case "/log":
                return Log(request);

            case "/card":
                return PageResponse.Text(200, Data.Card(request.Query("name") ?? request.Query("id") ?? string.Empty) ?? "no such card");

            case "/plan":
                return Plan(request);

            case "/request":
                return PageResponse.Text(200, Data.Request(request.Query("name") ?? string.Empty) ?? "no such request");

            case "/review":
                return Review(request);

            case "/repo-file":
                return RepoFile(request);

            case "/health":
                return PageResponse.Text(200, Data.HealthText() ?? "no health check yet");

            case "/live.json":
                Live.Ask();
                if (Live.BuildDue())
                {
                    Live.Build();
                }

                // 610-C: the answer is the last build trimmed to what this page has not seen; the
                // page says how far it has got per slot with ?since=4:1234,1:87.
                return PageResponse.Json(Live.Answer(request.Query("since")));

            case "/status.json":
                return PageResponse.Json(Data.StatusJson());

            default:
                return PageResponse.NotFound("not found");
        }
    }

    // 1113-C: a plan document from main, rendered with an anchor on every heading; an unknown path is a plain not-found page.
    private PageResponse Plan(PageRequest request)
    {
        var path = (request.Query("path") ?? string.Empty).Trim();
        var text = path.Length == 0 ? null : Data.PlanText(path);
        return text is null
            ? PageResponse.NotFound("not found: no plan at " + path)
            : PageResponse.Html(new Views.PlanPage().Render(path, text));
    }

    // Roadmap item 6 (650-C): the review panel of one card.
    private PageResponse Review(PageRequest request)
    {
        var panel = Data.Review(request.Query("card") ?? request.Query("id") ?? string.Empty);
        return panel is null
            ? PageResponse.NotFound("no such card")
            : PageResponse.Html(new Views.ReviewPage().Render(panel, Data.Snapshot().Health, request.IsLocal || RemoteControl));
    }

    // A repository file the panel links to (a screenshot or a text the Result names); the host
    // refuses a name that leaves the repository.
    private PageResponse RepoFile(PageRequest request)
    {
        var path = Data.RepoFile(request.Query("path") ?? string.Empty);
        if (path is null)
        {
            return PageResponse.NotFound("no such file");
        }

        var type = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "text/plain; charset=utf-8",
        };

        try
        {
            return PageResponse.File(System.IO.File.ReadAllBytes(path), type);
        }
        catch (IOException)
        {
            return PageResponse.NotFound("no such file");
        }
    }

    private PageResponse Font()
    {
        var path = Data.FontPath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return PageResponse.NotFound("no font");
        }

        var type = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".otf" => "font/otf",
            _ => "font/ttf",
        };

        try
        {
            return PageResponse.File(System.IO.File.ReadAllBytes(path), type);
        }
        catch (IOException)
        {
            return PageResponse.NotFound("no font");
        }
    }

    private PageResponse Log(PageRequest request)
    {
        var named = request.Query("file");
        string? file;
        if (!string.IsNullOrEmpty(named))
        {
            // Only a log this scheduler wrote: a card id (a number of any width and a category letter,
            // 931-A) and a stamp. Anything
            // else - a path, dots or slashes - is refused, so no request can read a file from
            // outside the log folder (PAGE-9).
            if (!LogName.IsMatch(named))
            {
                return PageResponse.NotFound(NoSuchLog);
            }

            file = System.IO.Path.Combine(Data.LogDirectory, named);
            if (!file.StartsWith(Data.LogDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return PageResponse.NotFound(NoSuchLog);
            }
        }
        else if (request.QueryInt("recent") is { } recent)
        {
            file = RecentLog(recent);
        }
        else if (request.QueryInt("slot") is { } slot)
        {
            file = Data.RunningWorkers().FirstOrDefault(worker => worker.Slot == slot)?.LogPath;
        }
        else
        {
            return PageResponse.Text(200, NoSuchLog);
        }

        if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file))
        {
            return PageResponse.Text(200, NoSuchLog);
        }

        var lines = LiveLog.TailFile(file, LiveLog.LogLines);
        return PageResponse.Text(200, string.Join('\n', lines.Select(LiveLog.Plain)));
    }

    private string? RecentLog(int index)
    {
        if (index < 0 || !Directory.Exists(Data.LogDirectory))
        {
            return null;
        }

        try
        {
            var files = Directory.GetFiles(Data.LogDirectory, "*.log")
                .OrderByDescending(path => System.IO.File.GetLastWriteTimeUtc(path))
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return index < files.Count ? files[index] : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // Every POST route answers at once (957-B). What only touches the in-memory state runs here -
    // there is nothing to wait for and it must not wait behind someone else's commit - and everything
    // that can reach git, a process or a file goes to the queue's one worker thread, which takes the
    // clicks in the order they came and runs one at a time.
    private PageResponse Post(PageRequest request) => request.Path switch
    {
        "/pause" => Now(request, "pause", null, Actions.Pause),
        "/resume" => Now(request, "resume", null, Actions.Resume),
        "/health-now" => Queued(request, "health", null, Actions.HealthNow),
        "/drain" => Now(request, "drain", null, Actions.Drain),
        "/planner-toggle" => Now(request, "planner", null, Actions.PlannerToggle),
        "/stopall" => Queued(request, "stopall", null, Actions.StopAll),
        "/tick" => Now(request, "tick", null, Actions.Tick),
        "/send" => SlotRoute(request, "send", slot => Actions.Send(
            slot, Collapse(request.Form("text"), 2000))),
        // A burst of answers to one card is applied in click order: they are three entries on the one
        // queue, taken oldest first (957-B item 5).
        "/answer" => AnswerRoute(request),
        // WT-P4 retires /worker-settings as well (card 937-B): the Worker types panel replaced the
        // per-worker settings, and no control on the page posts it.
        "/open-lanes" => Queued(request, "open-lanes", null, Actions.OpenCategories),
        "/open-workers" => Queued(request, "open-workers", null, Actions.OpenWorkers),
        "/accept-lane" => Queued(request, "lane", Category(request), () =>
            Actions.AcceptCategory(request.Query("lane") ?? request.Form("lane") ?? string.Empty)),
        // Roadmap item 1 (580-A): what to do with a generic file whose copy was edited in a project.
        "/template-overwrite" => Queued(request, "template", Template(request), () => Actions.TemplateOverwrite(
            Project(request), request.Query("file") ?? request.Form("file") ?? string.Empty)),
        "/template-keep" => Queued(request, "template", Template(request), () => Actions.TemplateKeep(
            Project(request), request.Query("file") ?? request.Form("file") ?? string.Empty)),
        "/open" => CardRoute(request, "open", Actions.Open),
        // Retry and Resume: the card leaves Waiting and the Queue shows it as resuming.
        "/resume-card" => CardRoute(request, "queue", card => Actions.ResumeCard(card, NoteOf(request))),
        "/done" => CardRoute(request, "done", card => Actions.Done(card, NoteOf(request))),
        "/retry" => CardRoute(request, "queue", card => Actions.Retry(card, NoteOf(request))),
        // 1099-C: the note box's own button files the note in the card without moving it.
        "/note" => CardRoute(request, "note", card => Actions.SaveNote(card, NoteOf(request))),
        // WT-P4: the slot routes are gone - /enable, /disable, /slots-add and /slots-remove have no
        // meaning once a worker is a type running in a folder (WT-P1, WT-P2).
        "/stop" => SlotRoute(request, "stop", Actions.Stop),
        "/kill" => SlotRoute(request, "kill", Actions.Kill),
        // The Worker types panel's Save (WT-P3) and one agent's Resume (WT-9).
        "/worker-types" => Queued(request, "types", null, () => Actions.SaveWorkerTypes(Total(request), Types(request))),
        // 992-B: the Categories panel's Save.
        "/categories" => Queued(request, "categories", null, () => Actions.SaveCategories(CategoryRows(request), Lines(request.Form("addonly") ?? string.Empty))),
        "/agent-resume" => Queued(request, "agent", Agent(request), () =>
            Actions.ResumeAgent(request.Query("agent") ?? request.Form("agent") ?? string.Empty)),

        // A POST for no route still goes back to the page, as it does today; nothing is queued.
        _ => PageResponse.SeeOther("/"),
    };

    // A card route: a click that names no card is refused at the door and says so, rather than
    // reaching the scheduler with an empty name (957-B item 1).
    private PageResponse CardRoute(PageRequest request, string kind, Func<string, ActionResult> work)
    {
        var card = Card(request);
        return card.Length == 0
            ? Refused(request, kind, card, "no card was named, so nothing was moved")
            : Queued(request, kind, card, () => work(card));
    }

    // A slot route, refused the same way when no worker is named.
    private PageResponse SlotRoute(PageRequest request, string kind, Func<int, ActionResult> work)
    {
        var slot = Slot(request);
        return slot < 1
            ? Refused(request, kind, null, "no worker was named, so nothing was stopped")
            : Queued(request, kind, "slot " + slot.ToString(System.Globalization.CultureInfo.InvariantCulture), () => work(slot));
    }

    // /answer: the card, the question, the choice and an optional note. Three clicks to one card are
    // three entries on the one queue, so they land in the order they were clicked (957-B item 5).
    private PageResponse AnswerRoute(PageRequest request)
    {
        var card = Card(request);
        if (card.Length == 0)
        {
            return Refused(request, "answer", null, "no card was named, so the answer went nowhere");
        }

        var question = request.FormInt("q") ?? request.QueryInt("q") ?? -1;
        var choice = request.Form("a") ?? request.Query("a") ?? string.Empty;
        var note = NoteOf(request);
        return Queued(request, "answer", card, () => Actions.Answer(card, question, choice, note));
    }

    // A pure state action: run it now, so the reply carries a state that has already changed.
    private PageResponse Now(PageRequest request, string kind, string? card, Func<ActionResult> work) =>
        Reply(request, Queue.Run(kind, Named(card), work));

    // Everything else: answer at once and let the one worker thread do the work in click order.
    private PageResponse Queued(PageRequest request, string kind, string? card, Func<ActionResult> work) =>
        Reply(request, Queue.Enqueue(kind, Named(card), work));

    // A request the router will not pass on: recorded as failed so the notice line says why.
    private PageResponse Refused(PageRequest request, string kind, string? card, string why) =>
        Reply(request, Queue.Refuse(kind, Named(card), why));

    private static PageResponse Reply(PageRequest request, int id) =>
        request.IsFetch
            ? PageResponse.Accepted("{\"id\":" + id.ToString(CultureInfo.InvariantCulture) + "}")
            : PageResponse.SeeOther("/");

    // The name the live feed shows beside the action; an empty one means it names nothing.
    private static string? Named(string? card) => string.IsNullOrEmpty(card) ? null : card;

    // 1099-C: the note typed in the card's one note box, whichever button sent it.
    private static string NoteOf(PageRequest request) => Collapse(request.Form("note") ?? request.Query("note"), 500);

    private static string Card(PageRequest request) =>
        request.Query("card") ?? request.Form("card") ?? string.Empty;

    private static string Category(PageRequest request) =>
        request.Query("lane") ?? request.Form("lane") ?? string.Empty;

    private static string Template(PageRequest request) =>
        request.Query("file") ?? request.Form("file") ?? string.Empty;

    private static string Agent(PageRequest request) =>
        request.Query("agent") ?? request.Form("agent") ?? string.Empty;

    // The project a control came from, on the combined page (580-A names it on the template routes).
    private static string Project(PageRequest request) =>
        request.Query("project") ?? request.Form("project") ?? string.Empty;

    private static int Slot(PageRequest request) => request.QueryInt("slot") ?? request.FormInt("slot") ?? 0;

    // The Worker types panel's total (WT-C1, WT-P3). A total below 1 means the field was cleared;
    // the scheduler's own floor is what WT-C1 says.
    private static int Total(PageRequest request) => Math.Max(1, request.FormInt("maxWorkers") ?? 1);

    /// <summary>
    /// The Worker types panel's rows (WT-P3): the fields of every row post a value, so the lists
    /// come in the same length and the same order as the rows on the page. A row with no name is not
    /// a type - it is a blank row from Add type that was never filled in.
    /// </summary>
    private static IReadOnlyList<PageWorkerTypeView> Types(PageRequest request)
    {
        var names = request.FormList("wname");
        var types = new List<PageWorkerTypeView>(names.Count);
        for (var n = 0; n < names.Count; n++)
        {
            var name = names[n].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            types.Add(new PageWorkerTypeView
            {
                Name = name,
                // 947-B: the name the row was loaded with, which is what makes a rename a rename.
                OriginalName = At(request.FormList("worig"), n).Trim(),
                Max = Math.Max(0, Number(At(request.FormList("wmax"), n))),
                Agent = At(request.FormList("wagent"), n).Trim(),
                Model = At(request.FormList("wmodel"), n).Trim(),
                Endpoint = At(request.FormList("wendpoint"), n).Trim(),
                MetricsUrl = At(request.FormList("wmetrics"), n).Trim(),
                Premium = At(request.FormList("wpremium"), n) is "1" or "yes" or "true",
                Projects = List(At(request.FormList("wprojects"), n)),
            });
        }

        return types;
    }

    // 992-B: the Categories panel's rows, read as lists of the same length. A row with neither a letter
    // nor a letter it was loaded with is an Add category row left empty, and is dropped; one with a
    // letter it was loaded with but none now is kept, so Save refuses it rather than removing it.
    private static IReadOnlyList<PageCategory> CategoryRows(PageRequest request)
    {
        var letters = request.FormList("cletter");
        var rows = new List<PageCategory>(letters.Count);
        for (var n = 0; n < letters.Count; n++)
        {
            var original = At(request.FormList("corig"), n).Trim();
            if (letters[n].Trim().Length == 0 && original.Length == 0)
            {
                continue;
            }

            rows.Add(new PageCategory
            {
                Letter = letters[n].Trim(),
                OriginalLetter = original,
                Name = At(request.FormList("cname"), n).Trim(),
                Purpose = At(request.FormList("cpurpose"), n).Trim(),
                Color = At(request.FormList("ccolor"), n).Trim(),
                Owns = Lines(At(request.FormList("cowns"), n)),
                Except = Lines(At(request.FormList("cexcept"), n)),
            });
        }

        return rows;
    }

    // One path or glob per line (992-B), blank lines dropped.
    private static IReadOnlyList<string> Lines(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string At(IReadOnlyList<string> values, int n) => n < values.Count ? values[n] : string.Empty;

    private static int Number(string? text) => int.TryParse(text, out var value) ? value : 0;

    // A list field: the names separated by commas, semicolons or spaces.
    private static IReadOnlyList<string> List(string text) =>
        [.. text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Collapse(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var one = Runs.Replace(text, " ").Trim();
        return one.Length > max ? one[..max] : one;
    }
}
