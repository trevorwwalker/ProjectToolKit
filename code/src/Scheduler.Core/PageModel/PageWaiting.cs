using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.PageModel;

/// <summary>
/// The rows of the Waiting on you window (PAGE-5; the window of schedule.ps1 L3564-3598), in the
/// order it shows them: held categories, the generic files whose copy was edited in the project
/// (roadmap item 1, job card 580-A), stuck cards, the cards 395-B flagged for a run that finished
/// without updating the card (410-A), cards parked as needs-human, cards waiting on the
/// developer, and review cards whose worker has finished. A waiting-on-developer card in
/// <c>active/</c> is also an orphan, so the Queue window lists it too (spec C15).
/// A card parked as needs-human can sit in <c>queue/</c> as well as <c>active/</c> (the planner or
/// the developer parked it there, and the dispatcher takes neither), so both folders are searched
/// for it (job card 1149-B). A card is listed once: a row an earlier rule of this window already
/// gave it is not repeated.
/// </summary>
public static class PageWaiting
{
    /// <summary>The note for a card that gives no reason for waiting (Why-Html, L3068).</summary>
    public const string NoReason = "No reason given; see the card's Result.";

    /// <summary>
    /// The note for a card whose run finished but whose text was never committed (395-B, job card 410-A).
    /// </summary>
    public const string FinishedWithoutUpdateNote =
        "finished without updating the card: its Result was not committed; check the run's log, fill in the card, then Done";

    /// <summary>The note on a generic file whose copy was edited in the project (roadmap item 1, job card 580-A).</summary>
    public static string EditedTemplateNote(string project, string target) =>
        $"{project}: {target} was edited in this project";

    /// <summary>
    /// The note on a stuck card's row (DISP-7, DISP-11; 940-A): how many of its runs ended without a
    /// result, and what the last one did when the host can name it. The row's Retry button follows it.
    /// </summary>
    public static string StuckNote(int runs, string lastResult = "") =>
        $"{runs} runs ended without a result" +
        (lastResult.Length == 0 ? " (stalled or killed)" : $" (last: {lastResult})") +
        ". Read its logs, fix the card, then Retry.";

    /// <summary>
    /// The window's rows. <paramref name="stuck"/> is what dispatch found stuck (DISP-7, DISP-11),
    /// <paramref name="heldCategories"/> the categories held until the developer accepts a change (DISP-5),
    /// <paramref name="running"/> the workers, whose cards hide their review row,
    /// <paramref name="finishedWithoutUpdate"/> the cards 395-B flagged for an uncommitted Result,
    /// which show as stuck and hide their own review row, and <paramref name="editedTemplates"/> the
    /// generic files whose copy was edited in a project, which wait here for Overwrite or Keep
    /// (roadmap item 1, job card 580-A). <paramref name="stuckRuns"/> is what the host knows of each
    /// stuck card's runs - the count and the last run's result and log - which is what tells the
    /// developer why the card is stuck (940-A).
    /// </summary>
    public static IReadOnlyList<PageWaitingItem> Window(
        IReadOnlyList<Card> cards,
        IReadOnlyCollection<Card>? stuck = null,
        IReadOnlyDictionary<string, string>? heldCategories = null,
        IReadOnlyCollection<RunningWorker>? running = null,
        int maxRetries = 2,
        IReadOnlyCollection<FinishedCardFlag>? finishedWithoutUpdate = null,
        IReadOnlyCollection<PageTemplateEdit>? editedTemplates = null,
        IReadOnlyCollection<PageStuckRun>? stuckRuns = null)
    {
        var items = new List<PageWaitingItem>();

        foreach (var category in (heldCategories ?? new Dictionary<string, string>(Text.Comparer)).Keys
                 .OrderBy(k => k, Text.SortComparer))
        {
            items.Add(new PageWaitingItem
            {
                Kind = PageWaitingKind.HeldCategory,
                Name = category,
                Note = heldCategories![category],
            });
        }

        // Roadmap item 1: a copy that was edited in the project is never overwritten silently. It
        // waits here until the developer takes the toolkit's template (Overwrite) or keeps the
        // project's own (Keep, which holds until the template changes again).
        foreach (var edit in (editedTemplates ?? [])
                 .OrderBy(e => e.Project, Text.SortComparer)
                 .ThenBy(e => e.Target, Text.SortComparer))
        {
            items.Add(new PageWaitingItem
            {
                Kind = PageWaitingKind.TemplateEdited,
                Name = edit.Target,
                Note = EditedTemplateNote(edit.Project, edit.Target),
            });
        }

        // A card is listed once: the first rule of this window to name it owns its row, so a later
        // rule skips it (1149-B). The review rows keep their own check against `shown` below.
        var listed = new HashSet<string>(Text.Comparer);

        foreach (var c in stuck ?? [])
        {
            listed.Add(c.Name);
            var run = stuckRuns?.FirstOrDefault(r => Text.Same(r.Card, c.Name) || Text.Same(r.Card, c.Id));
            items.Add(new PageWaitingItem
            {
                Kind = PageWaitingKind.Stuck,
                Name = c.Name,
                Note = StuckNote(run?.Runs is > 0 ? run.Runs : maxRetries + 1, run?.Result ?? ""),
                LastResult = run?.Result ?? "",
                Log = run?.Log ?? "",
            });
        }

        // 395-B caught a worker that moved its card but committed nothing: it waits here, with the
        // stuck rows, until main's copy moves on from the text it was flagged at (job card 410-A).
        var flags = finishedWithoutUpdate ?? [];
        var flagged = new HashSet<string>(flags.Select(f => f.Card), Text.Comparer);
        var shown = new List<string>();
        foreach (var c in cards.Where(c => flagged.Contains(c.Name) && Stays(c, flags))
                               .OrderBy(c => c.Num))
        {
            shown.Add(c.Name);
            listed.Add(c.Name);
            items.Add(new PageWaitingItem
            {
                Kind = PageWaitingKind.FinishedWithoutUpdate,
                Name = c.Name,
                Note = FinishedWithoutUpdateNote,
            });
        }

        // A worker parks its card by setting Status: needs-human; that is a question for the developer.
        // Both queue/ and active/ are searched (1149-B): the dispatcher takes a needs-human card from
        // either folder (DISP-2, DISP-9), so one parked in queue/ waits for nobody unless it is shown.
        foreach (var c in cards.Where(c => c.Folder is CardFolder.Queue or CardFolder.Active
                                        && Text.IsMatch(c.Status, "^needs-human")
                                        && !listed.Contains(c.Name))
                               .OrderBy(c => c.Num))
        {
            listed.Add(c.Name);
            items.Add(Row(PageWaitingKind.Question, c));
        }

        // Waits-On-Dev covers active/ as well as queue/, which is why an active one is in two windows.
        foreach (var c in cards.Where(c => CardWaits.OnDeveloper(c) && !listed.Contains(c.Name))
                               .OrderBy(c => c.Num))
        {
            listed.Add(c.Name);
            items.Add(Row(PageWaitingKind.Waiting, c));
        }

        // A card is listed once its worker has finished, so Done cannot race the worker's last push.
        // One its flagged-for-an-uncommitted-Result row already covers is not listed twice.
        var handled = new HashSet<string>(shown, Text.Comparer);
        var runIds = new HashSet<string>((running ?? Array.Empty<RunningWorker>()).Select(w => w.Id), Text.Comparer);
        foreach (var c in cards.Where(c => c.Folder == CardFolder.Review && !runIds.Contains(c.Id) && !handled.Contains(c.Name)))
        {
            items.Add(Row(PageWaitingKind.Review, c));
        }

        return items;
    }

    /// <summary>
    /// Whether <paramref name="card"/> is still the card that was flagged (job card 410-A): it sits in
    /// <c>review/</c>, and the flag naming it still describes main - the same folder, so the developer's
    /// Done (MOVE-6) has not moved it on, and the same blob, so no commit has rewritten it. A flag with
    /// no folder and no stamp can only ask the first. A card in <c>done/</c> waits for nothing (1054-B):
    /// nothing there moves it on or clears the row, and a worker that journalled its Result before
    /// asking for done with the same text (1034-B) is flagged without having lost anything.
    /// </summary>
    private static bool Stays(Card card, IReadOnlyCollection<FinishedCardFlag> flags) =>
        card.Folder == CardFolder.Review
        && flags.Any(f => Text.Same(f.Card, card.Name)
                          && (f.Folder is null || f.Folder == card.Folder)
                          && (f.Stamp.Length == 0 || card.BlobId.Length == 0 || Text.Same(f.Stamp, card.BlobId)));

    private static PageWaitingItem Row(PageWaitingKind kind, Card card) => new()
    {
        Kind = kind,
        Name = card.Name,
        Note = WhyOf(card),
        Questions = card.Questions,
    };

    /// <summary>Why-Html (L3067) without the markup: the card's reason, marked when it is a guess
    /// from its newest Result entry.</summary>
    private static string WhyOf(Card card) => card.Why switch
    {
        "" => NoReason,
        _ when Text.Same(card.WhyFrom, "result") => $"Result: {card.Why}",
        _ => card.Why,
    };
}
