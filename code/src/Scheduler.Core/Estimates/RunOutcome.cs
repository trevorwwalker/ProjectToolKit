// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Estimates;

/// <summary>What became of one run (EST-21): every row of accounting.csv has exactly one.</summary>
public enum RunOutcome
{
    /// <summary>The worker finished the card (result done).</summary>
    Done,

    /// <summary>The worker finished and the card needs the developer's hands (result review).</summary>
    Review,

    /// <summary>Parked with a question (result needs-human).</summary>
    Question,

    /// <summary>Sent to the planner (result waiting).</summary>
    Planner,

    /// <summary>Gave a file up to an older card (result yielded).</summary>
    Yielded,

    /// <summary>Stopped at the time limit.</summary>
    TimeLimit,

    /// <summary>Stopped by the developer, which is not the worker's doing.</summary>
    Stopped,

    /// <summary>The agent account was out of quota.</summary>
    OutOfQuota,

    /// <summary>The scheduler or the CLI failed to launch the worker: a 0-minute crash or early end.</summary>
    DidNotStart,

    /// <summary>Failed, stalled, crashed after starting, or ended with no result.</summary>
    Failed,

    /// <summary>The machine's doing (no log, model server down); shown as a count and nothing else.</summary>
    NotCounted,
}

/// <summary>EST-21: the outcome of a row, by its result word first and then its stop reason.</summary>
public static class RunOutcomes
{
    /// <summary>
    /// Words are compared ignoring case and <c>crashed</c> by its prefix (<c>crashed (exit 1)</c>).
    /// <c>crashed</c>, <c>no result line</c> and <c>ended early</c> with 0 minutes never started the worker.
    /// </summary>
    public static RunOutcome Of(string result, string stopReason, int minutes)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(stopReason);

        var word = result.Trim();
        if (word.StartsWith("crashed", StringComparison.OrdinalIgnoreCase))
        {
            return minutes == 0 ? RunOutcome.DidNotStart : RunOutcome.Failed;
        }

        if (Text.Same(word, "no result line") || Text.Same(word, "ended early"))
        {
            return minutes == 0 ? RunOutcome.DidNotStart : RunOutcome.Failed;
        }

        if (Text.Same(word, "done"))
        {
            return RunOutcome.Done;
        }

        if (Text.Same(word, "review"))
        {
            return RunOutcome.Review;
        }

        if (Text.Same(word, "needs-human"))
        {
            return RunOutcome.Question;
        }

        if (Text.Same(word, "waiting"))
        {
            return RunOutcome.Planner;
        }

        if (Text.Same(word, "yielded"))
        {
            return RunOutcome.Yielded;
        }

        if (Text.Same(word, "time limit"))
        {
            return RunOutcome.TimeLimit;
        }

        if (Text.Same(word, "stopped"))
        {
            return stopReason.Trim().StartsWith("over the", StringComparison.OrdinalIgnoreCase)
                ? RunOutcome.TimeLimit
                : RunOutcome.Stopped;
        }

        if (Text.Same(word, "stopped from page") || Text.Same(word, "killed"))
        {
            return RunOutcome.Stopped;
        }

        if (Text.Same(word, "out of quota"))
        {
            return RunOutcome.OutOfQuota;
        }

        if (Text.Same(word, "no log") || Text.Same(word, "model server down"))
        {
            return RunOutcome.NotCounted;
        }

        // failed, stalled, and any other word.
        return RunOutcome.Failed;
    }
}
