using Scheduler.Host.Observe;

namespace Scheduler.Host.Tests.Observe;

/// <summary><c>-Compare &lt;old log&gt; &lt;new log&gt;</c> (roadmap item 1, card 390-B).</summary>
public sealed class CompareCommandTests : IDisposable
{
    private readonly string _dir = Scheduler.TestSupport.TestTmp.Create("cmp");

    public void Dispose() => Directory.Delete(_dir, true);

    private string File(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void Disagreeing_logs_print_each_tick_and_exit_1()
    {
        // DISP-21, roadmap item 1
        var old = File("old.log", "[2026-11-18 10:00:20] slot 4 <- 390-B-x, pid 1, m via claude");
        var @new = File("new.log", "10:00:30 OBSERVE 2026-11-18 10:00:30 tick 1: state running, running none");
        var output = new StringWriter();

        var code = CompareCommand.Run(old, @new, output, new StringWriter());

        Assert.Equal(1, code);
        Assert.Contains("tick 1 at 2026-11-18 10:00:30", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("old only: slot 4 <- 390-B-x", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Agreeing_logs_exit_0()
    {
        // roadmap item 1
        var old = File("old.log", "[2026-11-18 10:00:00] x");
        var @new = File("new.log", "10:00:30 OBSERVE 2026-11-18 10:00:30 tick 1: state running, running none");
        var output = new StringWriter();

        Assert.Equal(0, CompareCommand.Run(old, @new, output, new StringWriter()));
        Assert.Contains("every tick agrees", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_log_exits_2()
    {
        // roadmap item 1
        var error = new StringWriter();

        Assert.Equal(2, CompareCommand.Run(Path.Combine(_dir, "none.log"), Path.Combine(_dir, "none2.log"), new StringWriter(), error));
        Assert.Contains("none.log", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_log_the_old_scheduler_holds_open_can_still_be_read()
    {
        // roadmap item 1: the comparison runs while the old scheduler is writing its log
        var old = File("old.log", "[2026-11-18 10:00:00] x");
        var @new = File("new.log", "10:00:30 OBSERVE 2026-11-18 10:00:30 tick 1: state running, running none");
        using var held = new FileStream(old, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        Assert.Equal(0, CompareCommand.Run(old, @new, new StringWriter(), new StringWriter()));
    }
}
