// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Runtime.InteropServices;
using Scheduler.Core.Contracts;
using System.Text.RegularExpressions;

namespace Scheduler.Host.Slots;

/// <summary>
/// Named Windows job objects (kernel32 through P/Invoke): a process put in one can be ended with
/// everything it started (WRK-13, TIME-11).
/// </summary>
public sealed partial class JobObjects
{
    /// <summary>Stands for the process id in a job name given before the process exists.</summary>
    public const string PidToken = "{pid}";

    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessSetQuota = 0x0100;
    private const uint JobAllAccess = 0x1F001F;

    private readonly Dictionary<string, nint> _handles = new();

    /// <summary>
    /// The job's name, <c>ProjectToolkit-&lt;project&gt;-&lt;slot|health&gt;-&lt;n&gt;-&lt;pid&gt;</c> with every
    /// character other than letters, digits, <c>_</c>, <c>.</c> and <c>-</c> replaced by <c>_</c> (WRK-13).
    /// </summary>
    public static string Name(string project, string what, int slot, int pid) =>
        Prefix(project, what, slot) + "-" + pid;

    /// <summary>
    /// The name up to the pid with <see cref="PidToken"/> in its place: what <see cref="IWorkerProcesses.Start"/>
    /// is given, because the pid is known only once the process has started.
    /// </summary>
    public static string Template(string project, string what, int slot) =>
        Prefix(project, what, slot) + "-" + PidToken;

    /// <summary>The name with the pid filled in.</summary>
    public static string Resolve(string nameOrTemplate, int pid) =>
        nameOrTemplate.Replace(PidToken, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string Prefix(string project, string what, int slot) =>
        NameChars().Replace($"ProjectToolkit-{project}-{what}-{slot}", "_");

    /// <summary>Put a running process in a new job called <paramref name="name"/>. False when that failed.</summary>
    public bool Enter(int pid, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        var job = CreateJobObjectW(0, name);
        if (job == 0)
        {
            return false;
        }
        var process = OpenProcess(ProcessTerminate | ProcessSetQuota, false, (uint)pid);
        var ok = process != 0 && AssignProcessToJobObject(job, process);
        if (process != 0)
        {
            CloseHandle(process);
        }
        // The handle stays open on purpose: a job lives while a handle or a process holds it.
        lock (_handles)
        {
            _handles[name] = job;
        }
        return ok;
    }

    /// <summary>End every process in the job, then let it go. Harmless when it is gone already.</summary>
    public void End(string name)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(name))
        {
            return;
        }
        nint job;
        lock (_handles)
        {
            if (!_handles.Remove(name, out job))
            {
                job = OpenJobObjectW(JobAllAccess, false, name);
            }
        }
        if (job != 0)
        {
            TerminateJobObject(job, 1);
            CloseHandle(job);
        }
    }

    /// <summary>Whether a job with this name exists.</summary>
    public bool Exists(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        var job = OpenJobObjectW(JobAllAccess, false, name);
        if (job == 0)
        {
            return false;
        }
        CloseHandle(job);
        return true;
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]")]
    private static partial Regex NameChars();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint attributes, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenJobObjectW(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(nint job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
