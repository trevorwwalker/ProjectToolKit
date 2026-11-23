using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 987-B: the one background writer every commit and push to main goes through, so the tick hands
/// git work over and carries on.
/// </summary>
public class GitWriterTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // The poster returns while the write (a slow push) is still running.
    [Fact]
    public void Post_ReturnsBeforeTheWriteFinishes()
    {
        using var writer = new GitWriter(new ListLog());
        using var push = new ManualResetEventSlim(false);
        var finished = false;

        writer.Post("planner step", () =>
        {
            push.Wait(Wait);
            finished = true;
            return false;
        });

        Assert.False(finished);
        Assert.False(writer.Idle);
        push.Set();
        Assert.True(writer.WaitIdle(Wait));
        Assert.True(finished);
    }

    // Writes land one at a time, in the order they were handed over.
    [Fact]
    public void Post_WritesLandInOrder()
    {
        using var writer = new GitWriter(new ListLog());
        var order = new List<string>();
        var running = 0;
        var overlapped = false;
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            writer.Post(name, () =>
            {
                overlapped |= Interlocked.Increment(ref running) > 1;
                Thread.Sleep(20);
                lock (order)
                {
                    order.Add(name);
                }

                Interlocked.Decrement(ref running);
                return false;
            });
        }

        Assert.True(writer.WaitIdle(Wait));
        Assert.Equal(["a", "b", "c", "d"], order);
        Assert.False(overlapped);
    }

    // A tick that posts the planner step while the last one is still waiting adds nothing: one is enough.
    [Fact]
    public void Post_SameKeyStillWaiting_IsNotQueuedTwice()
    {
        using var writer = new GitWriter(new ListLog());
        using var gate = new ManualResetEventSlim(false);
        var runs = 0;
        writer.Post("first", () => gate.Wait(Wait));

        Assert.True(writer.Post("planner step", () => Interlocked.Increment(ref runs) > 0));
        Assert.False(writer.Post("planner step", () => Interlocked.Increment(ref runs) > 0));
        gate.Set();

        Assert.True(writer.WaitIdle(Wait));
        Assert.Equal(1, runs);
    }

    // A failed write is reported in the log, the writer carries on, and the next post tries it again.
    [Fact]
    public void Post_FailedWrite_IsReportedAndRetried()
    {
        var log = new ListLog();
        using var writer = new GitWriter(log);
        var tries = 0;
        bool Push()
        {
            if (Interlocked.Increment(ref tries) == 1)
            {
                throw new IOException("push rejected");
            }

            return true;
        }

        writer.Post("planner step", Push);
        Assert.True(writer.WaitIdle(Wait));
        Assert.Contains(log.Lines, l => l.Contains("planner step", StringComparison.Ordinal) && l.Contains("push rejected", StringComparison.Ordinal));

        writer.Post("planner step", Push);
        Assert.True(writer.WaitIdle(Wait));
        Assert.Equal(2, tries);
    }

    // A write that moved main says so, so the tick can re-read the cards.
    [Fact]
    public void Post_WriteThatMovedMain_RaisesMoved()
    {
        using var moved = new ManualResetEventSlim(false);
        using var writer = new GitWriter(new ListLog()) { Moved = moved.Set };

        writer.Post("quiet", () => false);
        Assert.True(writer.WaitIdle(Wait));
        Assert.False(moved.IsSet);

        writer.Post("moves", () => true);
        Assert.True(moved.Wait(Wait));
    }

    // A caller that needs the result (a page action, a finish) waits its turn behind what was posted first.
    [Fact]
    public void Run_WaitsItsTurnAndReturnsTheResult()
    {
        using var writer = new GitWriter(new ListLog());
        var order = new List<string>();
        writer.Post("posted", () =>
        {
            Thread.Sleep(50);
            lock (order)
            {
                order.Add("posted");
            }

            return false;
        });

        var result = writer.Run("page action", () =>
        {
            lock (order)
            {
                order.Add("run");
            }

            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(["posted", "run"], order);
    }

    // Run on the writer's own thread (the planner step committing through the committer) runs at once.
    [Fact]
    public void Run_FromTheWriterThread_RunsInline()
    {
        using var writer = new GitWriter(new ListLog());
        var inner = 0;
        writer.Post("outer", () =>
        {
            inner = writer.Run("inner", () => 7);
            return false;
        });

        Assert.True(writer.WaitIdle(Wait));
        Assert.Equal(7, inner);
    }

    // A caller waiting on Run sees the write's exception.
    [Fact]
    public void Run_Exception_ReachesTheCaller()
    {
        using var writer = new GitWriter(new ListLog());
        var e = Assert.Throws<IOException>(() => writer.Run<int>("page action", () => throw new IOException("no network")));
        Assert.Equal("no network", e.Message);
    }
}
