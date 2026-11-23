using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Pool;

/// <summary>
/// The multi-project pool's dispatch over worker types (WT-7, spec B1 MP-2 to MP-7): while the total
/// has room, a free place is offered to the cards, and the card's fit picks the type. Pure: each
/// project's plan, the types and their counts come in; the starts go out through a callback.
/// </summary>
public static class PoolDispatcher
{
    /// <summary>
    /// MP-3, WRK-7, WT-11: threads per worker are the cores over the pool's <c>maxWorkers</c>:
    /// max(1, floor(cores / maxWorkers)), a size of 0 counting as one.
    /// </summary>
    public static int ThreadsPerWorker(int cores, int poolSize) => WorkerSelection.JobsPerWorker(cores, poolSize);

    /// <summary>1029-B: a project's workspaces: its pool file setting, or the pool's <c>maxWorkers</c> when it has none.</summary>
    public static int WorkspacesOf(int? workspaces, int maxWorkers) => workspaces ?? Math.Max(1, maxWorkers);

    /// <summary>1029-B: the project has a free workspace: fewer of its workers run than it has workspaces.</summary>
    public static bool HasFreeWorkspace(PoolProject project, int busy, int maxWorkers) =>
        busy < WorkspacesOf(project.Workspaces, maxWorkers);

    /// <summary>
    /// WT-3 (pool): type T fits card C of <paramref name="project"/> when T fits it as in one project
    /// (<see cref="TypeDispatcher.Fits"/>), serves the project (Q1) and the project defines T's agent (WT-C9).
    /// </summary>
    public static bool Fits(DispatchType type, PoolProject project, Card card, bool migrated) =>
        type.Serves(project.Name) && project.DefinesAgent(type) && TypeDispatcher.Fits(type, card, migrated);

    /// <summary>
    /// WT-7: the project's first candidate, in its own DISP-14 order and not in <paramref name="tried"/>,
    /// that has a usable type (WT-4), with the type WT-5 picks (the first listed). Null when it has none.
    /// <paramref name="running"/> holds the running count of every type by name; the total (WT-1) is the caller's.
    /// </summary>
    public static PoolOffer? Offer(
        PoolProject project,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        AgentQuota quota,
        ServerCache servers,
        IReadOnlyDictionary<string, int> running,
        ISet<string> tried)
    {
        foreach (var cand in project.Plan.Ordered)
        {
            if (tried.Contains(cand.Card.Id))
            {
                continue;
            }

            var type = types.FirstOrDefault(t => Fits(t, project, cand.Card, migrated)
                && TypeDispatcher.NotUsableBecause(t with { Running = running.GetValueOrDefault(t.Name, t.Running) }, quota, servers) is null);
            if (type is not null)
            {
                return new PoolOffer(project.Name, cand) { Type = type.Name };
            }
        }

        return null;
    }

    /// <summary>
    /// WT-7 (with MP-5 to MP-7): of the offers, resumes only when there is one (300-A Q3), then the project
    /// furthest below its share (300-A Q2); with one offer, that one outright. Priority does not jump the weights.
    /// </summary>
    public static PoolOffer? Choose(
        IReadOnlyList<PoolOffer> offers,
        Func<IReadOnlyList<PoolOffer>, PoolOffer> furthestBelowShare)
    {
        if (offers.Any(o => o.Candidate.Resume))
        {
            offers = [.. offers.Where(o => o.Candidate.Resume)];
        }

        return offers.Count switch
        {
            0 => null,
            1 => offers[0],
            _ => furthestBelowShare(offers),
        };
    }

    /// <summary>
    /// WT-7: while fewer than <paramref name="maxWorkers"/> workers run in all (WT-1, counted across every
    /// project, adopted ones included), every eligible project (<see cref="PoolGate.Eligible"/>) offers its
    /// first untried candidate with a usable type, unless all its workspaces are busy (1029-B); the choice is <see cref="Choose"/>; its card starts on the
    /// type WT-5 gave it. Repeats until no project has an offer or the total is reached. A resume counts its
    /// retry first (DISP-16); a start that fails is not tried again this tick.
    /// Returns the offers that started, in order.
    /// </summary>
    /// <param name="projects">The pool's projects, in the top-level config's order.</param>
    /// <param name="states">Each project's state; a project not named counts as running.</param>
    /// <param name="maxWorkers">The total (WT-C1).</param>
    /// <param name="totalRunning">Workers running now, in every project.</param>
    /// <param name="types">The pool's types in the config's order, with their running counts (all projects).</param>
    /// <param name="migrated">The types came from slots (<see cref="TypeDispatcher.WorkerFits"/>).</param>
    /// <param name="quota">The agents out of quota (WT-9), in every project.</param>
    /// <param name="servers">The model servers.</param>
    /// <param name="runs">The runs of the shares window (MP-6).</param>
    /// <param name="clock">Now, for the shares.</param>
    /// <param name="retries">Each project's retry counts by card id.</param>
    /// <param name="start">Starts the worker; true when it started.</param>
    /// <param name="lost">Filled with the offers that lost on the shares when the pool filled (MP-21).</param>
    public static IReadOnlyList<PoolOffer> Dispatch(
        IReadOnlyList<PoolProject> projects,
        IReadOnlyDictionary<string, SchedulerState> states,
        int maxWorkers,
        int totalRunning,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        AgentQuota quota,
        ServerCache servers,
        IEnumerable<PoolRun> runs,
        IClock clock,
        Func<string, IDictionary<string, int>> retries,
        Func<PoolOffer, bool> start,
        ICollection<PoolOffer>? lost = null)
    {
        var started = new List<PoolOffer>();
        var eligible = PoolGate.Eligible(projects, states);
        var running = types.ToDictionary(t => t.Name, t => t.Running, Text.Comparer);
        var shareRuns = runs.ToList();   // a start in this tick has run for no time yet, so it does not move the shares
        var tried = projects.ToDictionary(p => p.Name, _ => new HashSet<string>(Text.Comparer), Text.Comparer);
        var now = clock.Now;
        var total = totalRunning;
        var busy = projects.ToDictionary(p => p.Name, p => p.Busy, Text.Comparer);   // 1029-B: each project's workspaces in use

        var lastLosers = new List<PoolOffer>();
        while (total < maxWorkers)
        {
            var offers = new List<PoolOffer>();
            foreach (var project in eligible)
            {
                if (!HasFreeWorkspace(project, busy[project.Name], maxWorkers))
                {
                    continue;   // 1029-B: all its workspaces are busy; the other projects still offer
                }

                if (Offer(project, types, migrated, quota, servers, running, tried[project.Name]) is { } offer)
                {
                    offers.Add(offer);
                }
            }

            var chosen = Choose(offers, o => PoolShares.FurthestBelowShare(o, projects, shareRuns, now));
            if (chosen is null)
            {
                lastLosers.Clear();
                break;
            }

            var resumes = offers.Any(o => o.Candidate.Resume);
            lastLosers = [.. offers.Where(o => o != chosen && (!resumes || o.Candidate.Resume))];
            tried[chosen.Project].Add(chosen.Candidate.Card.Id);
            DispatchPlanner.CountResume(retries(chosen.Project), chosen.Candidate);
            if (!start(chosen))
            {
                continue;
            }

            running[chosen.Type]++;
            busy[chosen.Project]++;
            total++;
            started.Add(chosen);
        }

        // MP-21: the pool filled while these projects still offered; each lost on the shares.
        foreach (var l in lastLosers)
        {
            lost?.Add(l);
        }

        return started;
    }

    /// <summary>MP-21: the note for a project's card when no worker type's <c>projects</c> names the project.</summary>
    public static string NotServedNote(string project) => $"next up; no worker type serves project {project}";

    /// <summary>1029-B: the note for a project's card while every one of its workspaces is busy.</summary>
    public static string WaitsForWorkspaceNote(int busy, int workspaces) =>
        $"next up; waits for a workspace ({busy} of {workspaces} busy)";

    /// <summary>
    /// MP-21: the note for the card a project offered when the pool filled up with others' cards:
    /// its used share against its weight share among the offering projects, whole percents.
    /// </summary>
    public static string AboveShareNote(string project, double usedShare, double weightShare) =>
        $"next up; {project} is above its share ({Percent(usedShare)}% of {Percent(weightShare)}%)";

    /// <summary>MP-22: the dry-run line, with the project (WT-14).</summary>
    public static string DryRunLine(string project, string type, int slot, Card card, bool resume) =>
        $"DRY RUN {project} {type} in slot {slot} <- {card.Name}{(resume ? " (resume)" : "")}";

    private static int Percent(double share) => (int)Math.Round(share * 100, MidpointRounding.AwayFromZero);
}
