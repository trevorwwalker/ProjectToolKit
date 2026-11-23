using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

public class JobObjectTests
{
    // WRK-13: each worker runs in a named job object; ending the job ends every process in it.
    [Fact]
    public void Workers_JobObject_EndingKillsWholeTree()
    {
        Assert.True(OperatingSystem.IsWindows());
        using var tree = new ProcessTreeFixture("ProjectToolkit", 1);

        Assert.Equal(3, tree.Pids.Count);
        Assert.All(tree.Pids, pid => Assert.False(ProcessTreeFixture.Gone(pid)));
        Assert.Equal($"ProjectToolkit-ProjectToolkit-slot-1-{tree.RootPid}", tree.JobName);
        Assert.True(tree.Jobs.Exists(tree.JobName));

        tree.Jobs.End(tree.JobName);

        Assert.True(SpinWait(() => tree.Pids.All(ProcessTreeFixture.Gone)), "all three processes are gone");
        Assert.False(tree.Jobs.Exists(tree.JobName));
    }

    [Fact]
    public void JobName_ReplacesOddCharacters()
    {
        Assert.Equal("ProjectToolkit-My_Game-health-0-42", JobObjects.Name("My Game", "health", 0, 42));
        Assert.Equal("ProjectToolkit-a_b-slot-2-{pid}", JobObjects.Template("a b", "slot", 2));
        Assert.Equal("ProjectToolkit-a_b-slot-2-77", JobObjects.Resolve(JobObjects.Template("a b", "slot", 2), 77));
    }

    internal static bool SpinWait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return condition();
    }
}
