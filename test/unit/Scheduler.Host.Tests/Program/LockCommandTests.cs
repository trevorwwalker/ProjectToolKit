using Scheduler.Core.Tick;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Locks;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 1020-B (design <c>dynamic-locks.md</c>, section 3 and section 7's Host tests): the <c>lock</c> command
/// against a temporary log folder, with the scheduler's lock step played between its polls. Granted exits
/// 0, waiting then granted across two calls (one request only), yield exits 3, no answer exits 2.
/// </summary>
public class LockCommandTests
{
    private static Func<string, string?> Env(LockRig rig, string card, int slot) => name => name switch
    {
        "PTK_CARD" => card,
        "PTK_LOG_DIR" => rig.LogDir,
        "PTK_SLOT" => slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    private static int Lock(LockRig rig, Func<string, string?> env, StringWriter output, Action onSleep, int seconds = 110, params string[] args) =>
        LockCommand.Run(args, env, output, () => rig.Clock.Now, t => { rig.Clock.Advance(t); onSleep(); }, TimeSpan.FromSeconds(seconds));

    private static string LastLine(StringWriter w) => w.ToString().TrimEnd().Split('\n')[^1].Trim();

    private static int Requests(LockRig rig) =>
        Directory.Exists(Path.Combine(rig.Moves, "done")) ? Directory.GetFiles(Path.Combine(rig.Moves, "done"), "*.lock").Length : 0;

    [Fact]
    public void Lock_FreePath_GrantedExit0()
    {
        using var rig = new LockRig();
        var output = new StringWriter();

        var code = Lock(rig, Env(rig, LockRig.Old, 1), output, () => rig.RunStep(), 110, "docs/known_gaps.md", "--why", "a gap");

        Assert.Equal(0, code);
        Assert.Equal("granted", LastLine(output));
        Assert.Contains(rig.Row(LockRig.Old).Held, e => e.Path == "docs/known_gaps.md");
    }

    [Fact]
    public void Lock_WaitingThenGranted_AcrossTwoCalls_OneRequest()
    {
        using var rig = new LockRig();
        var env = Env(rig, LockRig.Old, 1);

        var first = new StringWriter();
        var code1 = Lock(rig, env, first, () => rig.RunStep(), 20, "src/b/x.cs", "--why", "needs it");
        Assert.Equal(2, code1);
        Assert.StartsWith("waiting: held by 200-B-young, gives up at ", LastLine(first), StringComparison.Ordinal);

        // The second call finds the open request; the holder finishes while it waits.
        var polls = 0;
        var second = new StringWriter();
        var code2 = Lock(rig, env, second, () =>
        {
            if (++polls == 3)
            {
                rig.Running.Remove(LockRig.Young);
                rig.Releases.RunEnded(LockRig.Young, "done");
            }

            rig.RunStep();
        }, 110, "src/b/x.cs", "--why", "needs it");

        Assert.Equal(0, code2);
        Assert.Equal("granted", LastLine(second));
        Assert.Equal(1, Requests(rig));
    }

    [Fact]
    public void Lock_YoungerCard_YieldExit3()
    {
        using var rig = new LockRig();
        var output = new StringWriter();

        var code = Lock(rig, Env(rig, LockRig.Young, 2), output, () => rig.RunStep(), 110, "src/a/x.cs", "--why", "needs it");

        Assert.Equal(3, code);
        Assert.Equal("yield: src/a/x.cs held by 100-A-old (older)", LastLine(output));
    }

    [Fact]
    public void Lock_NoAnswer_TimesOutPrintingAndExits2()
    {
        using var rig = new LockRig();
        var output = new StringWriter();

        var code = Lock(rig, Env(rig, LockRig.Old, 1), output, () => { }, 110, "docs/x.md", "--why", "w");

        Assert.Equal(2, code);
        Assert.StartsWith("waiting: no answer yet", LastLine(output), StringComparison.Ordinal);
        Assert.Contains("asked; no answer yet", output.ToString(), StringComparison.Ordinal);   // a line every 30 s
        Assert.Single(Directory.GetFiles(rig.Moves, "*.lock"));

        // Run again, it waits on the same unanswered request rather than writing another.
        var again = new StringWriter();
        Assert.Equal(0, Lock(rig, Env(rig, LockRig.Old, 1), again, () => rig.RunStep(), 110, "docs/x.md", "--why", "w"));
        Assert.Equal(1, Requests(rig));
    }

    [Fact]
    public void Lock_NoCardInTheEnvironment_RefusedExit4()
    {
        using var rig = new LockRig();
        var output = new StringWriter();

        var code = Lock(rig, _ => null, output, () => { }, 5, "docs/x.md");

        Assert.Equal(4, code);
        Assert.StartsWith("refused: unknown card", LastLine(output), StringComparison.Ordinal);
        Assert.False(Directory.Exists(rig.Moves));
    }

    [Fact]
    public void Lock_NoPaths_RefusedExit4()
    {
        using var rig = new LockRig();
        var output = new StringWriter();

        Assert.Equal(4, Lock(rig, Env(rig, LockRig.Old, 1), output, () => { }, 5, "--why", "nothing"));
        Assert.StartsWith("refused: no paths", LastLine(output), StringComparison.Ordinal);
    }

    [Fact]
    public void Lock_LogFolderAndSlotFromHooksPathAndTable_DL18()
    {
        using var rig = new LockRig();
        Func<string, string?> env = name => name switch
        {
            "PTK_CARD" => LockRig.Old,
            "GIT_CONFIG_COUNT" => "1",
            "GIT_CONFIG_KEY_0" => "core.hooksPath",
            "GIT_CONFIG_VALUE_0" => Path.Combine(rig.LogDir, "hooks"),
            _ => null,
        };
        var output = new StringWriter();

        Assert.Equal(0, Lock(rig, env, output, () => rig.RunStep(), 110, "docs/x.md", "--why", "w"));
        Assert.EndsWith("-slot1.lock", Directory.GetFiles(Path.Combine(rig.Moves, "done"), "*.lock").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void MovesWake_LockFileWakesLockRequest_MoveFileMoveRequest()
    {
        using var rig = new LockRig();
        Directory.CreateDirectory(rig.Moves);
        File.WriteAllText(Path.Combine(rig.Moves, "20261121-040000-000-slot1.move"), "x");
        Assert.Equal(TickWake.MoveRequest, MovesWake.Kind(rig.Moves));

        rig.Ask(LockRig.Old, 1, "docs/x.md");
        Assert.Equal(TickWake.LockRequest, MovesWake.Kind(rig.Moves));
    }
}
