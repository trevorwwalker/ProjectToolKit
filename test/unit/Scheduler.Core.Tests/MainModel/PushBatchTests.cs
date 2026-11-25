// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.MainModel;

namespace Scheduler.Core.Tests.MainModel;

public class PushBatchTests
{
    // MM-13: items ready together are one commit each and one push for all; a rejected push retries
    // the items one by one from the new main, in order; one bad item does not drop the others.
    [Fact]
    public void MM_13_OnePushForReadyItems_RejectedRetriesRemainingInOrder_EachResultOwn()
    {
        var steps = new List<string>();
        var outcomes = PushBatch.Run(
            ["a", "b", "c"],
            item =>
            {
                steps.Add("commit " + item);
                return true;
            },
            () =>
            {
                steps.Add("push");
                return true;
            },
            () => steps.Add("reset"));

        Assert.Equal([PushOutcome.Pushed, PushOutcome.Pushed, PushOutcome.Pushed], outcomes);
        Assert.Equal(["commit a", "commit b", "commit c", "push"], steps);

        // Rejected as a batch; then "b" cannot be committed on the new main, and "c"'s first push is
        // rejected once more; each item keeps its own result, in order.
        steps.Clear();
        var pushes = 0;
        var cTries = 0;
        outcomes = PushBatch.Run(
            ["a", "b", "c"],
            item =>
            {
                steps.Add("commit " + item);
                return item != "b" || steps.Count(s => s == "commit b") == 1;
            },
            () =>
            {
                steps.Add("push");
                pushes++;
                if (pushes == 1)
                {
                    return false;
                }

                if (steps[^2] == "commit c")
                {
                    cTries++;
                    return cTries > 1;
                }

                return true;
            },
            () => steps.Add("reset"));

        Assert.Equal([PushOutcome.Pushed, PushOutcome.CommitFailed, PushOutcome.Pushed], outcomes);
        Assert.Equal(
            [
                "commit a", "commit b", "commit c", "push",
                "reset", "commit a", "push",
                "reset", "commit b",
                "reset", "commit c", "push", "reset", "commit c", "push",
            ],
            steps);

        // An item that never gets pushed is PushFailed after its tries; a commit that fails in the
        // batch is not retried.
        outcomes = PushBatch.Run(["x", "y"], item => item == "x", () => false, () => { }, tries: 2);
        Assert.Equal([PushOutcome.PushFailed, PushOutcome.CommitFailed], outcomes);
    }
}
