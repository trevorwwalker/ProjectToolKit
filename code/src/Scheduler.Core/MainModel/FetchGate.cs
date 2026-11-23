using Scheduler.Core.Contracts;

namespace Scheduler.Core.MainModel;

/// <summary>Who asks the fetch gate (MM-9).</summary>
public enum FetchAsk
{
    /// <summary>A project's tick on its poll.</summary>
    Poll,

    /// <summary>The planner's pass.</summary>
    Planner,

    /// <summary>The health check's main sha.</summary>
    Health,

    /// <summary>The locks or scope audit.</summary>
    Audit,

    /// <summary>A page click: never fetches.</summary>
    Click,

    /// <summary>A worker's move request: never fetches.</summary>
    MoveRequest,
}

/// <summary>What the fetch gate did for one ask.</summary>
public enum FetchAnswer
{
    /// <summary>This ask ran the fetch and it worked.</summary>
    Fetched,

    /// <summary>This ask ran the fetch and it failed; the next ask fetches again.</summary>
    Failed,

    /// <summary>Answered by the last fetch (or the one in flight): it is fresh enough.</summary>
    Shared,

    /// <summary>A click or a move request: no fetch, by rule.</summary>
    Never,
}

/// <summary>
/// MM-9: one <c>git fetch</c> per repository per poll, shared by every consumer (the successor of
/// Host's <c>MainFetcher</c> and Core's <see cref="Cards.MainFetch"/>). An ask is answered by the
/// last successful fetch when it is younger than the poll interval; otherwise it fetches. The
/// scheduler's own pushes need no fetch: a push moves the local tracking ref, and the writer applies
/// the pushed commit to the model directly (MM-11). A fetch in flight answers every ask that waits on it. The first ask fetches; so does the first
/// after a worker ended (<see cref="WorkerEnded"/>, today's <c>Ask</c>) or after a failed fetch. A
/// click or a move request never fetches (1002-B). Host runs the fetch; this decides only when.
/// </summary>
public sealed class FetchGate(IClock clock, TimeSpan poll, Func<bool> fetch)
{
    private readonly Lock gate = new();
    private bool asked = true;   // the first ask always fetches
    private DateTimeOffset? lastFetch;

    /// <summary>Fetches run through this gate (for the step-cost log line, MM-18).</summary>
    public int Fetches { get; private set; }

    /// <summary>A worker ended: the next ask fetches, once, for all consumers.</summary>
    public void WorkerEnded()
    {
        lock (gate)
        {
            asked = true;
        }
    }

    /// <summary>Fetch if due for <paramref name="why"/>, else reuse the last fetch; see <see cref="FetchAnswer"/>.</summary>
    public FetchAnswer Ask(FetchAsk why)
    {
        if (why is FetchAsk.Click or FetchAsk.MoveRequest)
        {
            return FetchAnswer.Never;
        }

        lock (gate)
        {
            var now = clock.Now;
            if (!asked
                && lastFetch is { } last
                && now - last < poll)
            {
                return FetchAnswer.Shared;
            }

            Fetches++;
            if (!fetch())
            {
                asked = true;
                return FetchAnswer.Failed;
            }

            asked = false;
            lastFetch = now;
            return FetchAnswer.Fetched;
        }
    }
}
