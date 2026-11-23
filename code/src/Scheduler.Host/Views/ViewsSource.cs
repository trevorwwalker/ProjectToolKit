using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;

namespace Scheduler.Host.Views;

/// <summary>
/// 1090-B (views.md section 6 Host; VW-20 to VW-22): one project's views data, built from what is already
/// in memory - the open cards and the done and archive headers of the repository's model of main
/// (<see cref="IMainModel.DoneCards"/>, VW-21: no git, no reader of its own), the cards running in the
/// page's slots, and the plan files' text from the model (blobs under the jobs folder; watched paths
/// elsewhere, MM-6; the roadmap included). It is rebuilt only when the model's commit, its done-loaded
/// state or the running cards move (VW-22), on the scheduler thread after the page's refresh, never on a
/// page request; the page reads <see cref="Current"/>, an immutable record, without locks.
/// Not yet: the runs from accounting.csv and the typicals (Board and Gantt).
/// </summary>
/// <param name="project">The project's name.</param>
/// <param name="jobsDir">The project's jobs folder, relative to the repository (<c>jobs</c>).</param>
/// <param name="model">The repository's model of main, or null while it has not been made.</param>
/// <param name="slots">The project's page slots (the newest page snapshot's).</param>
/// <param name="roadmap">The roadmap's path, whose <c>## n.</c> items are plans (VW-13); "" for none.</param>
public sealed partial class ViewsSource(
    string project,
    string jobsDir,
    Func<IMainModel?> model,
    Func<IReadOnlyList<PageSlotView>> slots,
    string roadmap = ViewsSource.Roadmap)
{
    /// <summary>ProjectToolkit's roadmap (VW-13); a project without it simply has no roadmap items.</summary>
    public const string Roadmap = "docs/design/gdd/scheduler-roadmap.md";

    private readonly string _jobs = jobsDir.Trim('/');
    private readonly Dictionary<string, PlanText> _byBlob = new(StringComparer.Ordinal);
    private volatile ProjectViews _current = ProjectViews.Empty(project, ProjectViews.Starting);
    private string? _key;

    /// <summary>The newest views data; "starting..." until the model has loaded.</summary>
    public ProjectViews Current => _current;

    /// <summary>How many times the snapshot was built (VW-22: not when nothing moved).</summary>
    public int Builds { get; private set; }

    /// <summary>Why the last build failed (the previous snapshot was kept), or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Rebuild when the model's commit, its done-loaded state or the running cards moved (VW-22); otherwise
    /// keep the snapshot. True when it was rebuilt. A failure keeps the previous one (views.md section 5).
    /// </summary>
    public bool Refresh()
    {
        var main = model();
        var at = main?.Snapshot;
        if (main is null || at is null)
        {
            return false;
        }

        var running = RunningIds(slots());
        var key = at.Commit + "|" + at.DoneLoaded + "|" + string.Join(",", running.Order(StringComparer.Ordinal));
        if (key == _key)
        {
            return false;
        }

        try
        {
            _current = Build(main, at, running);
            _key = key;
            Builds++;
            LastError = null;
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            LastError = e.Message;
            return false;
        }
    }

    /// <summary>The ids (<c>1090-B</c>) of the cards the slots run: a slot's card may be named in full.</summary>
    public static HashSet<string> RunningIds(IEnumerable<PageSlotView> slots)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (CardId().Match(slot.Card) is { Success: true } m)
            {
                _ = ids.Add(m.Value);
            }
        }

        return ids;
    }

    private ProjectViews Build(IMainModel main, MainSnapshot at, HashSet<string> running)
    {
        List<ViewCard> cards =
        [
            .. at.Cards(_jobs).Select(ViewCard.From),
            .. at.DoneCards(_jobs).Select(ViewCard.From),
        ];

        // VW-12: what is on main - the jobs folder's files from the tree, then each document elsewhere that a
        // card names, read as a watched path (MM-6); one that is on main nowhere stays missing.
        var entries = at.Entries(_jobs);
        var onMain = new HashSet<string>(entries.Select(e => e.Path), StringComparer.Ordinal);
        var elsewhere = new Dictionary<string, string>(StringComparer.Ordinal);
        var wanted = cards.Select(c => PlanPaths.Resolve(c.PlanPath, onMain))
            .Where(r => r.Kind == PlanKind.Missing && !InJobs(r.Path))
            .Select(r => r.Path)
            .Append(roadmap)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal);
        foreach (var path in wanted)
        {
            if (main.Settings(path) is { } text)
            {
                elsewhere[path] = text;
                _ = onMain.Add(path);
            }
        }

        // VW-11: every *_plan.md in plans/ and archive/, and every path a card names.
        var refs = new Dictionary<string, PlanRef>(StringComparer.Ordinal);
        foreach (var e in entries.Where(e => IsPlanFile(e.Path)))
        {
            refs[e.Path] = PlanPaths.Resolve(e.Path, onMain);
        }

        foreach (var c in cards)
        {
            var r = PlanPaths.Resolve(c.PlanPath, onMain);
            if (r.Kind != PlanKind.NoPlan)
            {
                refs[r.Path] = r;
            }
        }

        var blobs = entries.ToDictionary(e => e.Path, e => e.BlobId, StringComparer.Ordinal);
        var plans = new List<ViewPlan>();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in refs.Values)
        {
            if (r.Kind == PlanKind.Missing)
            {
                plans.Add(new ViewPlan { Path = r.Path, Kind = r.Kind, Title = Path.GetFileName(r.Path), Status = "" });
                continue;
            }

            var text = blobs.TryGetValue(r.Path, out var blob) ? OfBlob(main, r.Path, blob) : PlanText.Of(elsewhere.GetValueOrDefault(r.Path));
            if (text.Text is { } planText)
            {
                texts[r.Path] = planText;
            }

            plans.Add(new ViewPlan { Path = r.Path, Kind = r.Kind, Title = text.Title, Status = text.Status });
            if (text.RoadmapItem is { } n)
            {
                items[r.Path] = n;
            }
        }

        // 1114-C: the roadmap is one plan whose "## n." items are its steps (a card naming it is already listed).
        if (roadmap.Length > 0 && elsewhere.TryGetValue(roadmap, out var roadmapText) && plans.All(p => p.Path != roadmap))
        {
            plans.Add(new ViewPlan { Path = roadmap, Kind = PlanKind.Elsewhere, Title = "Roadmap", Status = PlanStatus.Of(roadmapText) });
            texts[roadmap] = roadmapText;
        }

        return new ProjectViews
        {
            Project = project,
            Snapshot = ViewsSnapshot.Build(project, cards, plans, texts),
            Running = running,
            OnMain = onMain,
            RoadmapItemOf = items,
            DoneLoaded = at.DoneLoaded,
            Note = at.DoneLoaded ? "" : ProjectViews.ReadingDone,
            Commit = at.Commit,
        };
    }

    // A plan file under the jobs folder, read in process by blob (MM-4) once per blob id.
    private PlanText OfBlob(IMainModel main, string path, string blob)
    {
        if (!_byBlob.TryGetValue(blob, out var text))
        {
            text = PlanText.Of(main.Text(path));
            _byBlob[blob] = text;
        }

        return text;
    }

    private bool InJobs(string path) => path.StartsWith(_jobs + "/", StringComparison.Ordinal);

    private bool IsPlanFile(string path) =>
        (path.StartsWith(_jobs + "/plans/", StringComparison.Ordinal) || path.StartsWith(_jobs + "/archive/", StringComparison.Ordinal))
        && path.EndsWith("_plan.md", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\d+-[A-Za-z]+")]
    private static partial Regex CardId();

    // What the views keep of a plan file: its title (the first "# " line), status line and roadmap item.
    private sealed record PlanText(string Title, string Status, int? RoadmapItem, string? Text)
    {
        public static PlanText Of(string? text)
        {
            var title = (text ?? "").Split('\n').Select(l => l.TrimEnd('\r'))
                .FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? "";
            return new PlanText(title, PlanStatus.Of(text), PlanStatus.RoadmapItemOf(text), text);
        }
    }
}
