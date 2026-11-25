// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Tick;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 1190-B (request #020, MM-13): the claims of one dispatch tick are written at once and applied in one turn on the git
/// writer, so they reach main in one push, not one each. A hook on the temporary bare remote counts the pushes.
/// </summary>
public sealed class ClaimBatchTickTests
{
    private const string NeverEnds = "echo started\r\nping -n 600 127.0.0.1 >nul\r\n";

    private const string Second = "011-A-y";

    [Fact]
    public void TwoClaimsOfOneTick_OnePush_BothWorkersStart()
    {
        var cards = new Dictionary<string, string>
        {
            [$"jobs/queue/{TickRig.CardName}.md"] = TickRig.CardText,
            [$"jobs/queue/{Second}.md"] = "# 011-A: y\n\nLane: A\nStatus: ready\n",
        };
        using var rig = new TickRig(NeverEnds, cards: cards, slots: 2);
        var hooks = Path.Combine(rig.Fx.Bare, "hooks");
        Directory.CreateDirectory(hooks);
        File.WriteAllText(Path.Combine(hooks, "pre-receive"), "#!/bin/sh\necho x >> \"$GIT_DIR/pushes.log\"\nexit 0\n");
        var tick = rig.Composition.Tick;
        using var gate = new ManualResetEventSlim(false);
        rig.Composition.Host.Writer!.Post("hold the writer while the tick posts its claims", () =>
        {
            gate.Wait(TimeSpan.FromMinutes(5)); // a fallback only: the test opens the gate
            return false;
        });

        tick.Run();
        Assert.Equal(2, tick.Starting.Count);

        gate.Set();
        Assert.True(rig.TickUntilSettled(() => tick.Running.Count == 2), string.Join("\n", rig.Log.Lines));
        Assert.True(rig.RemoteHas($"jobs/active/{TickRig.CardName}.md"));
        Assert.True(rig.RemoteHas($"jobs/active/{Second}.md"));
        var log = Path.Combine(rig.Fx.Bare, "pushes.log");
        Assert.Equal(1, File.Exists(log) ? File.ReadAllLines(log).Length : 0);
    }
}
