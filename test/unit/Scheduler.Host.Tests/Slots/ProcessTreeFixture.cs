using System.Diagnostics;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

/// <summary>
/// Starts a worker command that spawns a child that spawns a grandchild, each sleeping, and
/// reports their process ids (a fake CLI for the job object tests).
/// </summary>
internal sealed class ProcessTreeFixture : IDisposable
{
    private readonly TempDir _dir = new();

    public WorkerProcesses Processes { get; }
    public JobObjects Jobs { get; } = new();
    public string Template { get; }
    public string JobName { get; }
    public int RootPid { get; }
    public IReadOnlyList<int> Pids { get; }

    public ProcessTreeFixture(string project, int slot)
    {
        Processes = new WorkerProcesses(Jobs);
        Template = JobObjects.Template(project, "slot", slot);
        File.WriteAllText(Path.Combine(_dir.Path, "child.ps1"),
            "$g = Start-Process ping -ArgumentList '-n','120','127.0.0.1' -PassThru -WindowStyle Hidden\r\n" +
            "Set-Content -Path pids2.tmp -Value $g.Id\r\n" +
            "Move-Item pids2.tmp pids2.txt\r\n" +
            "Start-Sleep 120");
        File.WriteAllText(Path.Combine(_dir.Path, "worker.ps1"),
            "$c = Start-Process powershell -ArgumentList '-NoProfile','-File','child.ps1' -PassThru -WindowStyle Hidden\r\n" +
            "Set-Content -Path pids1.tmp -Value $c.Id\r\n" +
            "Move-Item pids1.tmp pids1.txt\r\n" +
            "Start-Sleep 120");

        var started = Processes.Start("powershell -NoProfile -File worker.ps1", Template, _dir.Path, new Dictionary<string, string?>())
            ?? throw new InvalidOperationException("could not start the fake worker");
        RootPid = started.Pid;
        JobName = JobObjects.Name(project, "slot", slot, RootPid);

        // Wait until the child and the grandchild have started and written their ids.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        string[] files = [Path.Combine(_dir.Path, "pids1.txt"), Path.Combine(_dir.Path, "pids2.txt")];
        while (!files.All(File.Exists))
        {
            if (DateTime.UtcNow > deadline)
            {
                Processes.EndJob(Template, RootPid);
                throw new TimeoutException("the fake worker's children did not start");
            }
            Thread.Sleep(200);
        }
        Pids = [RootPid, .. files.Select(f => int.Parse(File.ReadAllText(f).Trim()))];
    }

    public static bool Gone(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        Processes.EndJob(Template, RootPid);
        _dir.Dispose();
    }
}
