// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Accounting;
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
/// 1131-B: the runs from accounting.csv (<see cref="RunLog"/>, read from where the last read stopped) and the
/// running slots join the snapshot, and its version follows them.
/// 1143-B (VW-19): the snapshot also carries the Gantt's worker cap (the project's workspaces, capped by the pool's
/// maxWorkers) and each category's typical run length per model, worked out from the runs already read (not again
/// from the file) and only when new runs arrive.
/// </summary>
/// <param name="project">The project's name.</param>
/// <param name="jobsDir">The project's jobs folder, relative to the repository (<c>jobs</c>).</param>
/// <param name="model">The repository's model of main, or null while it has not been made.</param>
/// <param name="slots">The project's page slots (the newest page snapshot's).</param>
public sealed partial class ViewsSource(
    string project,
    string jobsDir,
    Func<IMainModel?> model,
    Func<IReadOnlyList<PageSlotView>> slots,
    string? accountingPath = null,
    Func<int>? workspaces = null,
    Func<int>? maxWorkers = null,
    Func<DateTimeOffset>? now = null)
{
    private readonly RunLog? _log = accountingPath is { Length: > 0 } ? new RunLog(accountingPath) : null;

    private readonly string _jobs = jobsDir.Trim('/');
    private readonly Dictionary<string, PlanText> _byBlob = new(StringComparer.Ordinal);
    private volatile ProjectViews _current = ProjectViews.Empty(project, ProjectViews.Starting);
    private string? _key;
    private IReadOnlyList<AreaTypical>? _typicals;

    /// <summary>The newest views data; "starting..." until the model has loaded.</summary>
    public ProjectViews Current => _current;

    /// <summary>How many times the snapshot was built (VW-22: not when nothing moved).</summary>
    public int Builds { get; private set; }

    /// <summary>Why the last build failed (the previous snapshot was kept), or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>1213-B: the marks clicked and not yet on main, laid over plan-marks.json; null when none are kept.</summary>
    public PendingPlanMarks? PendingMarks { get; set; }

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

        var slotList = slots();
        var running = RunningIds(slotList);
        var live = RunningRuns(slotList);
        // VW-22: the commit, the done-loaded state, accounting.csv's length and last write, and each worker's start.
        var key = at.Commit + "|" + at.DoneLoaded + "|" + string.Join(",", running.Order(StringComparer.Ordinal))
            + "|" + (_log?.Stamp() ?? "") + "|"
            + string.Join(",", live.Select(r => r.CardId + "@" + r.Start.UtcTicks + "/" + r.Slot + "/" + r.Model + "/" + r.WorkerType + "/" + r.TimeBoxEnd?.UtcTicks))
            + "|";
        if (key + PendingMarks?.Version == _key)
        {
            return false;
        }

        try
        {
            if ((_log?.Refresh() ?? false) || _typicals is null)
            {
                _typicals = TypicalsOf(_log?.Runs ?? [], (now ?? (() => DateTimeOffset.Now))());
            }

            _current = Build(main, at, running, live);
            _key = key + PendingMarks?.Version;   // 1213-B: after the build, which may have pruned the pending marks
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

    /// <summary>The card id (<c>1090-B</c>) at the start of a card's name, or "".</summary>
    public static string CardIdOf(string card) => CardId().Match(card) is { Success: true } m ? m.Value : "";

    /// <summary>1131-B: the runs going now, one per slot with a card: its start, time limit, model and type.</summary>
    public static List<ViewRun> RunningRuns(IEnumerable<PageSlotView> slots) =>
    [
        .. slots.Where(s => CardIdOf(s.Card).Length > 0 && s.Start is not null).Select(s => new ViewRun
        {
            CardId = CardIdOf(s.Card),
            Start = s.Start!.Value,
            Model = s.Model,
            WorkerType = s.Type,
            Slot = s.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimeBoxEnd = s.TimeLimit is { } min ? s.Start.Value.AddMinutes(min) : null,
        }),
    ];

    /// <summary>
    /// VW-19: the simulated workers - the project's workspaces (the page's slots unless given), capped by the pool's
    /// maxWorkers when there is one; never fewer than one.
    /// </summary>
    private int WorkerCap(int slotCount)
    {
        var cap = workspaces?.Invoke() ?? slotCount;
        if (maxWorkers?.Invoke() is { } max and > 0)
        {
            cap = Math.Min(cap, max);
        }

        return Math.Max(1, cap);
    }

    /// <summary>
    /// 1143-B: the runs that finished a card (done or review) as <see cref="AreaRun"/>s - the area is the card id's
    /// letter - and their typical lengths per category and model (<see cref="AreaDurations.Typical"/>), over the
    /// last <see cref="AreaDurations.WindowDays"/> days.
    /// </summary>
    public static IReadOnlyList<AreaTypical> TypicalsOf(IEnumerable<ViewRun> runs, DateTimeOffset now)
    {
        var first = now.AddDays(-AreaDurations.WindowDays);
        List<AreaRun> list =
        [
            .. runs.Where(r => r.End is { } end && end >= first && r.Model.Length > 0 && r.CardId.Contains('-', StringComparison.Ordinal)
                    && (string.Equals(r.Result, "done", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Result, "review", StringComparison.OrdinalIgnoreCase)))
                .Select(r => new AreaRun
                {
                    Area = r.CardId[(r.CardId.IndexOf('-', StringComparison.Ordinal) + 1)..],
                    Model = r.Model,
                    Minutes = (int)Math.Round((r.End!.Value - r.Start).TotalMinutes),
                    Finished = r.End!.Value,
                }),
        ];
        return AreaDurations.Typical(list);
    }

    private ProjectViews Build(IMainModel main, MainSnapshot at, HashSet<string> running, List<ViewRun> live)
    {
        List<ViewCard> cards =
        [
            .. at.Cards(_jobs).Select(ViewCard.From),
            .. at.DoneCards(_jobs).Select(ViewCard.From),
        ];

        // VW-12: what is on main - the jobs folder's files from the tree. Only files in jobs/plans/ are plans (1126-C).
        var entries = at.Entries(_jobs);
        var onMain = new HashSet<string>(entries.Select(e => e.Path), StringComparer.Ordinal);

        // VW-11: every *_plan.md in plans/, and every plan path a card names.
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
        foreach (var r in refs.Values)
        {
            if (r.Kind == PlanKind.Missing)
            {
                plans.Add(new ViewPlan { Path = r.Path, Kind = r.Kind, Title = Path.GetFileName(r.Path), Status = "" });
                continue;
            }

            var text = blobs.TryGetValue(r.Path, out var blob) ? OfBlob(main, r.Path, blob) : PlanText.Of(null);
            if (text.Text is { } planText)
            {
                texts[r.Path] = planText;
            }

            plans.Add(new ViewPlan { Path = r.Path, Kind = r.Kind, Title = text.Title, Status = text.Status });
        }

        return new ProjectViews
        {
            Project = project,
            Snapshot = ViewsSnapshot.Build(project, cards, plans, texts, [.. _log?.Runs ?? [], .. live], WorkerCap(slots().Count), _typicals ?? [], Marks(main, onMain)),
            Running = running,
            OnMain = onMain,
            DoneLoaded = at.DoneLoaded,
            Note = at.DoneLoaded ? "" : ProjectViews.ReadingDone,
            Commit = at.Commit,
        };
    }

    // 1213-B: main's plan-marks.json with the pending clicks over it; a pending mark main now says is dropped.
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> Marks(IMainModel main, IReadOnlySet<string> onMain)
    {
        var rel = _jobs + "/" + PlanMarkFile.FileName;
        var marks = PlanMarkFile.Read(onMain.Contains(rel) ? main.Text(rel) : null);
        if (PendingMarks is not { } pending)
        {
            return marks;
        }

        pending.Prune(marks, (now ?? (() => DateTimeOffset.Now))());
        return pending.Over(marks);
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

    private bool IsPlanFile(string path) =>
        path.StartsWith(_jobs + "/plans/", StringComparison.Ordinal) && path.EndsWith("_plan.md", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\d+-[A-Za-z]+")]
    private static partial Regex CardId();

    // What the views keep of a plan file: its title (the first "# " line), and status line.
    private sealed record PlanText(string Title, string Status, string? Text)
    {
        public static PlanText Of(string? text)
        {
            var title = (text ?? "").Split('\n').Select(l => l.TrimEnd('\r'))
                .FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? "";
            return new PlanText(title, PlanStatus.Of(text), text);
        }
    }
}
