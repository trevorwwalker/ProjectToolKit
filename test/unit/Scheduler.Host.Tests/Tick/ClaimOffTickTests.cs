// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// 995-B: the claim at dispatch goes to the git writer. A dispatch tick returns while the claim waits
/// behind a slow write, the slot shows the card as starting, and the worker starts only once the
/// claim is on main (CM-9).
/// </summary>
public sealed class ClaimOffTickTests
{
    private const string NeverEnds = "echo started\r\nping -n 600 127.0.0.1 >nul\r\n";

    [Fact]
    public void DispatchTick_ReturnsBeforeTheClaimPush_WorkerStartsAfterItLands()
    {
        using var rig = new TickRig(NeverEnds);
        var tick = rig.Composition.Tick;
        using var gate = new ManualResetEventSlim(false);
        var slowWriteDone = 0;
        rig.Composition.Host.Writer!.Post("slow write ahead of the claim", () =>
        {
            // Only the test opens the gate. The timeout is a fallback that turns a regression
            // (a tick that waits for the claim push) into a failing assert instead of a hang.
            gate.Wait(TimeSpan.FromMinutes(5)); // 1026-B: 2 min was within reach of a loaded PC's first tick
            Volatile.Write(ref slowWriteDone, 1);
            return false;
        });

        tick.Run();

        // No wall-clock limit: the claim is queued behind a write the test still holds, so a tick
        // that returns now has, by construction, not waited for the claim push.
        Assert.True(Volatile.Read(ref slowWriteDone) == 0, "the dispatch tick waited for the git writer");
        Assert.Empty(tick.Running);
        Assert.Equal(TickRig.CardName, tick.Starting[1].Card);
        Assert.False(rig.RemoteHas($"jobs/active/{TickRig.CardName}.md"));

        tick.Run();   // still waiting: no worker, no second claim
        Assert.Empty(tick.Running);
        Assert.Single(tick.Starting);

        gate.Set();
        Assert.True(rig.TickUntilSettled(() => tick.Running.Count == 1), string.Join("\n", rig.Log.Lines));
        Assert.Empty(tick.Starting);
        Assert.True(rig.RemoteHas($"jobs/active/{TickRig.CardName}.md"));
        Assert.False(rig.RemoteHas($"jobs/queue/{TickRig.CardName}.md"));
        Assert.Single(rig.Log.Lines, l => l.Contains($"started: {TickRig.CardName}", StringComparison.Ordinal));
    }
}
