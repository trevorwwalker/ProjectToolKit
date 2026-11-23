using Scheduler.Core.MainModel;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>1066-B (state-cache.md MM-13): steps of one owner that wait together go in one push, in their order.</summary>
public class GitWriterPushTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private sealed class Owner
    {
        public List<string> Steps { get; } = [];

        public GitWriter.PushStep Step(string name, List<PushOutcome> outcomes, bool pushOk = true, Action? onCommit = null) => new(
            this,
            () =>
            {
                lock (Steps)
                {
                    Steps.Add("commit " + name);
                }

                onCommit?.Invoke();
                return true;
            },
            () =>
            {
                lock (Steps)
                {
                    Steps.Add("push");
                }

                return pushOk;
            },
            () =>
            {
                lock (Steps)
                {
                    Steps.Add("reset");
                }
            },
            o =>
            {
                lock (outcomes)
                {
                    outcomes.Add(o);
                }
            });
    }

    // MM-13: three steps of one repository that wait behind a running write are three commits and one push, in order.
    [Fact]
    public void MM_13_StepsWaitingTogether_AreOnePushInTheirOrder()
    {
        using var writer = new GitWriter(new ListLog());
        using var gate = new ManualResetEventSlim(false);
        var owner = new Owner();
        var outcomes = new List<PushOutcome>();
        writer.Post("busy", () =>
        {
            gate.Wait(Wait);
            return false;
        });

        // Each step on a thread of its own: RunPushed blocks once its step is in the queue, so a waiting thread is a
        // queued step. (A fixed sleep per step let a loaded PC start the write before the others had queued.)
        var threads = new List<Thread>();
        var errors = new List<Exception>();
        foreach (var name in new[] { "a", "b", "c" })
        {
            var step = owner.Step(name, outcomes);
            var thread = new Thread(() =>
            {
                try
                {
                    writer.RunPushed("step " + name, step);
                }
                catch (Exception e)
                {
                    lock (errors)
                    {
                        errors.Add(e);
                    }
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, Wait));
            threads.Add(thread);
        }

        gate.Set();
        Assert.All(threads, t => Assert.True(t.Join(Wait)));
        Assert.Empty(errors);
        Assert.Equal(["reset", "commit a", "commit b", "commit c", "push"], owner.Steps);
        Assert.Equal([PushOutcome.Pushed, PushOutcome.Pushed, PushOutcome.Pushed], outcomes);
    }

    // MM-13: steps of different owners are different repositories: never in one push.
    [Fact]
    public async Task MM_13_StepsOfOtherOwners_AreNotInOnePush()
    {
        using var writer = new GitWriter(new ListLog());
        using var gate = new ManualResetEventSlim(false);
        var one = new Owner();
        var two = new Owner();
        var outcomes = new List<PushOutcome>();
        writer.Post("busy", () =>
        {
            gate.Wait(Wait);
            return false;
        });

        var s1 = one.Step("a", outcomes);
        var s2 = two.Step("b", outcomes);
        var t1 = Task.Run(() => writer.RunPushed("one", s1));
        Thread.Sleep(150);
        var t2 = Task.Run(() => writer.RunPushed("two", s2));
        Thread.Sleep(150);
        gate.Set();
        await Task.WhenAll(t1, t2).WaitAsync(Wait);
        Assert.Equal(["reset", "commit a", "push"], one.Steps);
        Assert.Equal(["reset", "commit b", "push"], two.Steps);
    }

    // MM-13: a rejected push redoes the steps one by one from the new main, each with its own result.
    [Fact]
    public void MM_13_RejectedPush_RetriesOneByOne_EachResultItsOwn()
    {
        var owner = new Owner();
        var outcomes = new List<PushOutcome>();
        var pushes = 0;
        var steps = new List<GitWriter.PushStep>();
        foreach (var name in new[] { "a", "b" })
        {
            steps.Add(new GitWriter.PushStep(
                owner,
                () =>
                {
                    owner.Steps.Add("commit " + name);
                    return true;
                },
                () =>
                {
                    pushes++;
                    owner.Steps.Add("push");
                    return pushes > 1;
                },
                () => owner.Steps.Add("reset"),
                outcomes.Add));
        }

        var result = GitWriter.RunGroup(steps);
        Assert.Equal(
            ["reset", "commit a", "commit b", "push", "reset", "commit a", "push", "reset", "commit b", "push"],
            owner.Steps);
        Assert.Equal([PushOutcome.Pushed, PushOutcome.Pushed], result);
        Assert.Equal(result, outcomes);
    }
}
