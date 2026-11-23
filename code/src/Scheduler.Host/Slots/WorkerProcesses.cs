using System.Diagnostics;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Slots;

/// <summary>
/// <see cref="IWorkerProcesses"/> for Windows: workers run under <c>cmd /c</c> in a named job
/// object (WRK-13) and are ended with their whole tree (TIME-11).
/// </summary>
public sealed class WorkerProcesses : IWorkerProcesses
{
    private readonly JobObjects _jobs;
    private readonly Dictionary<int, Process> _started = new();

    /// <summary>Use <paramref name="jobs"/> for the job objects.</summary>
    public WorkerProcesses(JobObjects jobs) => _jobs = jobs;

    /// <inheritdoc />
    public StartedProcess? Start(string commandLine, string jobName, string workingDirectory,
        IReadOnlyDictionary<string, string?> environment)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c " + commandLine,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // 1073-B: every git in the worker's tree is non-interactive; the caller's own entries may still override.
        _ = GitNonInteractive.Apply(psi);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var (key, value) in environment)
        {
            if (value is null)
            {
                psi.Environment.Remove(key);
            }
            else
            {
                psi.Environment[key] = value;
            }
        }
        try
        {
            var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }
            lock (_started)
            {
                _started[process.Id] = process;
            }
            _jobs.Enter(process.Id, JobObjects.Resolve(jobName, process.Id));
            return new StartedProcess { Pid = process.Id, Start = new DateTimeOffset(process.StartTime) };
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAlive(int pid, DateTimeOffset start)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && Math.Abs((new DateTimeOffset(p.StartTime) - start).TotalSeconds) <= 2;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? CmdStartTime(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ? new DateTimeOffset(p.StartTime) : null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void EndJob(string jobName, int pid)
    {
        _jobs.End(JobObjects.Resolve(jobName, pid));
        KillTree(pid);
    }

    /// <inheritdoc />
    public int? ExitCode(int pid)
    {
        Process? p;
        lock (_started)
        {
            _started.TryGetValue(pid, out p);
        }
        try
        {
            return p is { HasExited: true } ? p.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public long? LogSize(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    /// <inheritdoc />
    public DateTimeOffset? LogLastWrite(string path) => File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTime(path)) : null;

    // taskkill /T can fail part-way; .NET's own tree walk then stops whatever descendants remain.
    private static void KillTree(int pid)
    {
        try
        {
            using var taskkill = Process.Start(new ProcessStartInfo("taskkill.exe", $"/PID {pid} /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            taskkill?.StandardOutput.ReadToEnd();
            taskkill?.StandardError.ReadToEnd();
            taskkill?.WaitForExit(30000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(true);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
