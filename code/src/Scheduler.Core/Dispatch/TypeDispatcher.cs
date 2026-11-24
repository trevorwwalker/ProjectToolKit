using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Dispatch;

/// <summary>
/// The second half of a dispatch decision, by worker type (WT-1 to WT-6, WT-8, WT-10 to WT-14;
/// worker-types.md): each candidate of a <see cref="DispatchPlan"/> goes to the first listed type
/// that fits it and has room, while fewer than <c>maxWorkers</c> run in all. It is the only
/// dispatcher: the slot form (DISP-15, DISP-17 to DISP-19) was removed once Host ran types (939-A).
/// </summary>
public static class TypeDispatcher
{
    /// <summary>
    /// WT-3 (<c>Worker:</c>): no line, or a blank one (<see cref="IsBlank"/>) -> the pool's default
    /// type only (1146-A); otherwise the type's name, its effective model, <c>Model-Short</c> of it or models.json's short.
    /// A value of only digits names the type migrated from that slot, and only while
    /// <paramref name="migrated"/> (the pool form is not yet saved; 530-D Q3).
    /// </summary>
    public static bool WorkerFits(DispatchType type, Card card, bool migrated)
    {
        var req = (card.WorkerReq ?? "").Trim();
        if (IsBlank(req))
        {
            return type.IsDefault;
        }

        if (Text.IsMatch(req, @"^\d+$"))
        {
            return migrated && int.TryParse(req, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && type.Slots.Contains(n);
        }

        return Text.Same(req, type.Name)
            || (type.Model.Length > 0 && (Text.Same(req, type.Model) || type.ModelNames.Any(m => Text.Same(req, m))));
    }

    /// <summary>
    /// 1146-A: a <c>Worker:</c> value that asks for no type: empty, or the card template's own text
    /// ("one worker type by name or model (optional; ...)") left in place.
    /// </summary>
    public static bool IsBlank(string? req)
    {
        var r = (req ?? "").Trim();
        return r.Length == 0 || r.StartsWith("one worker type by name or model", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>WT-3: the type fits the card by its <c>Worker:</c> line alone (969-A); the card's category does not matter.</summary>
    public static bool Fits(DispatchType type, Card card, bool migrated) =>
        WorkerFits(type, card, migrated);

    /// <summary>
    /// WT-4: why a type that fits cannot start a worker now, in the order WT-12 checks them, or null
    /// when it is usable. The total (WT-1) is not the type's and is checked by the caller. A full type
    /// names the cards its workers hold (WT-12, 935-A, the reason 800-B put into DISP-19).
    /// </summary>
    public static string? NotUsableBecause(DispatchType type, AgentQuota quota, ServerCache servers)
    {
        if (type.Max <= 0)
        {
            return $"{type.Name} is off";
        }

        if (type.Running >= type.Max)
        {
            return $"{type.Name} ({type.Running} of {type.Max} running{Held(type)})";
        }

        if (quota.Since(type.Agent) is { } since)
        {
            return $"{type.Name}: {AgentName(type)} out of quota since {since:HH:mm}";
        }

        return servers.IsUp(type.Endpoint) ? null : $"{type.Name}: model server does not answer";
    }

    /// <summary>
    /// The type a queued card is headed for (WT-12, roadmap item 4): the first listed type that fits
    /// and is not off, whatever is running, out of quota or down. Null when none would take it.
    /// </summary>
    public static DispatchType? Fitting(IReadOnlyList<DispatchType> types, Card card, bool migrated)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(card);
        return types.FirstOrDefault(t => t.Max > 0 && Fits(t, card, migrated));
    }

    /// <summary>WT-14: the dry-run line.</summary>
    public static string DryRunLine(string type, int slot, Card card, bool resume) =>
        $"DRY RUN {type} in slot {slot} <- {card.Name}{(resume ? " (resume)" : "")}";

    /// <summary>
    /// Dispatch (WT-1, WT-2, WT-5, WT-6, WT-8, WT-12): only while <paramref name="state"/> is running
    /// and fewer than <paramref name="maxWorkers"/> workers run in all (<paramref name="totalRunning"/>
    /// counts adopted ones and ones of removed types, WT-13), each of <see cref="DispatchPlan.Ordered"/>
    /// goes to the first listed usable type that fits it. A candidate with no usable type is skipped
    /// and the next goes; its scope stays taken (WT-8, the plan's). A resume counts in
    /// <paramref name="retries"/> before <paramref name="start"/> is called; a start that fails is not
    /// tried again this tick.
    /// </summary>
    /// <param name="plan">The first half: the candidates in order and their notes.</param>
    /// <param name="state">The scheduler's state.</param>
    /// <param name="maxWorkers">The total (WT-C1).</param>
    /// <param name="totalRunning">Workers running now, of any type.</param>
    /// <param name="types">The types in the config's order, with their running counts.</param>
    /// <param name="migrated">The types came from slots (see <see cref="WorkerFits"/>).</param>
    /// <param name="quota">The agents out of quota (WT-9).</param>
    /// <param name="servers">The model servers, cached 20 s.</param>
    /// <param name="retries">The retry counts by card id.</param>
    /// <param name="start">Starts the worker; true when it started.</param>
    public static TypeDispatchResult Dispatch(
        DispatchPlan plan,
        SchedulerState state,
        int maxWorkers,
        int totalRunning,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        AgentQuota quota,
        ServerCache servers,
        IDictionary<string, int> retries,
        Func<TypeStart, bool> start)
    {
        var running = types.ToDictionary(t => t.Name, t => t.Running, Text.Comparer);
        var total = totalRunning;
        var starts = new List<TypeStart>();
        var tried = new HashSet<string>(Text.Comparer);

        if (state == SchedulerState.Running)
        {
            foreach (var cand in plan.Ordered)
            {
                if (total >= maxWorkers)
                {
                    break;
                }

                var type = types.FirstOrDefault(t => Fits(t, cand.Card, migrated)
                    && NotUsableBecause(t with { Running = running[t.Name] }, quota, servers) is null);
                if (type is null)
                {
                    continue;
                }

                DispatchPlanner.CountResume(retries, cand);
                var attempt = new TypeStart(type.Name, cand, Started: false);
                var ok = start(attempt);
                if (ok)
                {
                    running[type.Name]++;
                    total++;
                }

                starts.Add(attempt with { Started = ok });
                tried.Add(cand.Card.Id);
            }
        }

        return new TypeDispatchResult { Starts = starts, Notes = Notes(plan, tried, state, maxWorkers, total, types, migrated, running, quota, servers) };
    }

    /// <summary>
    /// WT-12: a note for each candidate of <paramref name="plan"/> not in <paramref name="tried"/>, then
    /// the plan's own notes. <paramref name="running"/> is each type's running count after the starts.
    /// </summary>
    public static List<DispatchNote> Notes(
        DispatchPlan plan,
        ISet<string> tried,
        SchedulerState state,
        int maxWorkers,
        int total,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        IReadOnlyDictionary<string, int> running,
        AgentQuota quota,
        ServerCache servers,
        Func<Card, string?>? before = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var notes = new List<DispatchNote>();
        foreach (var cand in plan.Candidates.Where(c => !tried.Contains(c.Card.Id)))
        {
            var note = before?.Invoke(cand.Card) ?? NotStartedNote(cand.Card, state, maxWorkers, total, types, migrated, running, quota, servers);
            notes.Add(new(cand.Card.Name, cand.Resume ? $"orphan to resume; {note}" : note));
        }

        notes.AddRange(plan.Notes);
        return notes;
    }

    /// <summary>WT-12: why a candidate was not started, without the orphan prefix, in the order the rule gives.</summary>
    private static string NotStartedNote(
        Card card,
        SchedulerState state,
        int maxWorkers,
        int total,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        IReadOnlyDictionary<string, int> running,
        AgentQuota quota,
        ServerCache servers)
    {
        if (state != SchedulerState.Running)
        {
            return $"next up; dispatching is {StateWord(state)}";
        }

        var fitting = types.Where(t => Fits(t, card, migrated)).ToList();
        if (fitting.Count == 0)
        {
            return IsBlank(card.WorkerReq)
                ? "next up; no default worker type exists"
                : $"next up; asks for worker {card.WorkerReq}, and no worker type fits";
        }

        var why = fitting
            .Select(t => NotUsableBecause(t with { Running = running.GetValueOrDefault(t.Name, t.Running) }, quota, servers))
            .Where(r => r is not null)
            .ToList();
        return why.Count > 0
            ? $"next up; waiting for {string.Join(", ", why)}"
            : $"next up; waiting for a free worker ({total} of {maxWorkers} running)";
    }

    private static string AgentName(DispatchType type) => type.Agent.Length == 0 ? "the default agent" : type.Agent;

    /// <summary>
    /// WT-12 (935-A): the cards a full type holds, as the caller gave them (slot order), up to three
    /// then "+n"; nothing when the caller does not say what runs.
    /// </summary>
    private static string Held(DispatchType type)
    {
        var held = type.RunningCards.Where(n => n.Length > 0).ToList();
        if (held.Count == 0)
        {
            return "";
        }

        var named = string.Join(", ", held.Take(3));
        return held.Count > 3 ? $": {named}+{held.Count - 3}" : $": {named}";
    }

    private static string StateWord(SchedulerState state) => state switch
    {
        SchedulerState.Running => "running",
        SchedulerState.Paused => "paused",
        SchedulerState.PausedRed => "paused-red",
        SchedulerState.PausedQuota => "paused-quota",
        SchedulerState.Draining => "draining",
        _ => state.ToString().ToLowerInvariant(),
    };
}
