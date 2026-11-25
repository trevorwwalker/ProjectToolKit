// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.MainModel;

/// <summary>What became of one item of a <see cref="PushBatch"/>.</summary>
public enum PushOutcome
{
    /// <summary>Its commit is on the remote.</summary>
    Pushed,

    /// <summary>Its change could not be committed (on any try); the others went on.</summary>
    CommitFailed,

    /// <summary>Its commit was made but every push of it was rejected or failed.</summary>
    PushFailed,
}

/// <summary>
/// MM-13: items ready together for one repository go in one push. Each item is still its own
/// commit, made in order; one push sends them all. If that push is rejected, the items are retried
/// from the new main one by one, in order, as the writer does today (up to <c>tries</c> each: reset
/// to main, commit, push), so one bad item never drops the others; each item's result is its own.
/// The git steps are the caller's delegates (Host's <c>GitWriter</c>); this decides only the order.
/// </summary>
public static class PushBatch
{
    /// <summary>
    /// Runs <paramref name="items"/>: <paramref name="commit"/> makes one item's commit on the
    /// current state (false: it could not), <paramref name="push"/> pushes everything committed
    /// (false: rejected or failed), <paramref name="reset"/> takes the new main and drops unpushed
    /// commits. Returns one outcome per item, in the items' order.
    /// </summary>
    public static IReadOnlyList<PushOutcome> Run<T>(
        IReadOnlyList<T> items,
        Func<T, bool> commit,
        Func<bool> push,
        Action reset,
        int tries = 3)
    {
        var outcomes = new PushOutcome[items.Count];
        var committed = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (commit(items[i]))
            {
                committed.Add(i);
            }
            else
            {
                outcomes[i] = PushOutcome.CommitFailed;
            }
        }

        if (committed.Count == 0)
        {
            return outcomes;
        }

        if (push())
        {
            foreach (var i in committed)
            {
                outcomes[i] = PushOutcome.Pushed;
            }

            return outcomes;
        }

        foreach (var i in committed)
        {
            outcomes[i] = PushOutcome.PushFailed;
            for (var attempt = 0; attempt < tries; attempt++)
            {
                reset();
                if (!commit(items[i]))
                {
                    outcomes[i] = PushOutcome.CommitFailed;
                    break;
                }

                if (push())
                {
                    outcomes[i] = PushOutcome.Pushed;
                    break;
                }
            }
        }

        return outcomes;
    }
}
