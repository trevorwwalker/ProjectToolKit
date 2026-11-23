using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

public class KillTests
{
    // TIME-11 (Host part): a kill ends the job and the whole process tree, and killing again is
    // harmless. Keeping the first StopWhy and the log line belong to Core's kill (card 170).
    [Fact]
    public void Time_Kill_EndsTreeKeepsFirstReason()
    {
        using var tree = new ProcessTreeFixture("ProjectToolkit", 2);
        Assert.All(tree.Pids, pid => Assert.False(ProcessTreeFixture.Gone(pid)));

        tree.Processes.EndJob(tree.Template, tree.RootPid);

        Assert.True(JobObjectTests.SpinWait(() => tree.Pids.All(ProcessTreeFixture.Gone)), "whole tree gone");
        Assert.False(tree.Jobs.Exists(tree.JobName));
        tree.Processes.EndJob(tree.Template, tree.RootPid);
        Assert.False(tree.Processes.IsAlive(tree.RootPid, DateTimeOffset.Now));
    }
}
