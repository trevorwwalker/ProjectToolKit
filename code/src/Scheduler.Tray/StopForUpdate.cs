// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Scheduler.Tray;

/// <summary>
/// What <c>-StopForUpdate</c> sees of the kit and how it asks each part to end (INS-19), so the sequence can be
/// tested without real processes. The kit is the one folder the stop was run from (1258-B): a Switchyard running
/// from any other folder is not part of it and is left alone.
/// </summary>
public interface IKitProcesses
{
    /// <summary>Ask this kit's running trays to exit leaving the workers running. False when none listens.</summary>
    bool SignalTrays();

    /// <summary>Take the trays' exit request back, so a tray started after the update stays up.</summary>
    void ResetTrays();

    /// <summary>How many of this kit's trays are running, this process not counted.</summary>
    int Trays();

    /// <summary>Ask the pool's scheduler to end by itself, as Restart does. False when no pool scheduler runs.</summary>
    bool SignalPool();

    /// <summary>How many processes run from the kit's <c>scheduler-next.exe</c>: schedulers and workers' <c>lock</c> or <c>check-push</c> calls.</summary>
    int ExeProcesses();

    /// <summary>The other folders a <c>scheduler-next.exe</c> runs from now: other Switchyards, left alone.</summary>
    IReadOnlyList<string> OtherKits();
}

/// <summary>
/// INS-19 and INS-22 (card 1245-B): <c>scheduler-tray.exe -StopForUpdate</c>, which an installer runs before it
/// replaces the kit's files. It ends the running trays and the pool's scheduler the way Restart ends a scheduler
/// (the workers keep running and the next scheduler adopts them, TRAY-5, TRAY-8), waits for workers'
/// <c>lock</c> and <c>check-push</c> calls from the exe to end (up to <see cref="SchedulerUpdater.CallsWindow"/>,
/// as 1021-B), and says in one line what it did: exit code 0 when nothing of the kit is running any more,
/// 1 otherwise.
/// </summary>
public sealed class StopForUpdate(IKitProcesses kit, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>
    /// How long the trays are given to exit: each ends its scheduler first, the way Exit, leave workers running
    /// does, which waits up to 20 s for it; a tray looks for the request twice a second.
    /// </summary>
    public static readonly TimeSpan TraysWindow = TimeSpan.FromSeconds(45);

    /// <summary>How often the stop looks again while something is still running.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    /// <summary>Exit code when nothing of the kit is running any more.</summary>
    public const int Stopped = 0;

    /// <summary>Exit code when something of the kit is still running.</summary>
    public const int NotStopped = 1;

    /// <summary>Stop the kit and say how it went.</summary>
    public async Task<(int Code, string Line)> RunAsync(CancellationToken cancel)
    {
        var wait = delay ?? Task.Delay;
        var said = new List<string>();
        var others = kit.OtherKits();
        try
        {
            if (kit.SignalTrays())
            {
                said.Add("the tray was asked to exit");
                await WaitAsync(() => kit.Trays() == 0, TraysWindow, wait, cancel).ConfigureAwait(false);
            }

            // A tray running or attached to the pool has ended it by now; a pool started without a tray has not.
            // There is one pool on the machine and its shutdown event is named by the pool file, which every
            // Switchyard shares (1258-B): set it only when the pool can be this kit's alone - a scheduler runs from
            // this folder and none from another. Otherwise another folder's pool is left alone, and a pool of
            // this kit's that is still running is reported below.
            if (others.Count == 0 && kit.ExeProcesses() > 0 && kit.SignalPool())
            {
                said.Add("the pool's scheduler was asked to end");
            }

            await WaitAsync(() => kit.ExeProcesses() == 0, SchedulerUpdater.CallsWindow, wait, cancel).ConfigureAwait(false);
        }
        finally
        {
            kit.ResetTrays();
        }

        var trays = kit.Trays();
        var left = kit.ExeProcesses();
        var alone = LeftAlone(others);
        if (trays > 0)
        {
            return (NotStopped, $"stop for update: {Count(trays, "tray")} still running; close {(trays == 1 ? "it" : "them")} from the icon's menu and try again{alone}");
        }

        if (left > 0)
        {
            return (NotStopped, $"stop for update: {Count(left, "process")} still running from scheduler-next.exe after {(int)SchedulerUpdater.CallsWindow.TotalSeconds} s (a scheduler, or a worker's lock or check-push call); try again when {(left == 1 ? "it has" : "they have")} ended{alone}");
        }

        return (Stopped, said.Count == 0
            ? $"stop for update: nothing of the kit was running{alone}"
            : $"stop for update: {string.Join(" and ", said)}; nothing of the kit is running now and the workers keep running{alone}");
    }

    // 1258-B: another folder's Switchyard is said in the one line and is not a failure.
    private static string LeftAlone(IReadOnlyList<string> others) =>
        string.Concat(others.Select(folder => $"; another Switchyard runs from {folder}; left alone"));

    private static async Task WaitAsync(Func<bool> done, TimeSpan window, Func<TimeSpan, CancellationToken, Task> wait, CancellationToken cancel)
    {
        var waited = TimeSpan.Zero;
        while (!done() && waited < window)
        {
            await wait(Poll, cancel).ConfigureAwait(false);
            waited += Poll;
        }
    }

    private static string Count(int n, string what) => n == 1 ? $"1 {what} is" : $"{n} {what}{(what.EndsWith('s') ? "es" : "s")} are";
}

/// <summary>
/// The tray's own exit signal (1245-B): a named event every tray holds open while it runs and looks at on its
/// timer. When <c>-StopForUpdate</c> sets it, the tray exits the way "Exit, leave workers running" does. The
/// event is the user's session's alone (no <c>Global\</c> prefix), and its name is keyed by the folder of the
/// <c>scheduler-next.exe</c> the tray runs (1258-B), so a stop run from one kit folder never reaches a tray of
/// another: every tray runs from the same copy (967-B), so that folder is the only thing that tells them apart.
/// </summary>
public sealed class TrayExitSignal : IDisposable
{
    /// <summary>The start of every tray exit signal's name.</summary>
    public const string Prefix = "ProjectToolkit-tray-exit-";

    private readonly EventWaitHandle _event;

    private TrayExitSignal(EventWaitHandle handle) => _event = handle;

    /// <summary>The event's name for the trays that run the scheduler in <paramref name="schedulerDir"/>.</summary>
    public static string NameFor(string schedulerDir)
    {
        var full = Path.GetFullPath(schedulerDir).TrimEnd('\\', '/').ToUpperInvariant();
        return Prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
    }

    /// <summary>
    /// Open the signal for a tray to watch, creating it when no other tray of its folder has. A test names its
    /// own event, so it never asks a real tray on the machine to exit.
    /// </summary>
    public static TrayExitSignal Listen(string name) => new(new EventWaitHandle(false, EventResetMode.ManualReset, name));

    /// <summary>Whether an exit has been asked for.</summary>
    public bool IsSet => _event.WaitOne(0);

    /// <inheritdoc/>
    public void Dispose() => _event.Dispose();
}

/// <summary>
/// The kit as it runs on this machine, for <see cref="StopForUpdate"/>: the one folder of
/// <paramref name="schedulerExe"/> (1258-B). Its schedulers and calls are the processes whose exe is that file; its
/// trays are the ones holding that folder's <see cref="TrayExitSignal"/> open (they all run from one copy, 967-B, so
/// their own exe says nothing). <paramref name="running"/> lists the full exe paths of the running processes of a
/// name (a test gives its own).
/// </summary>
public sealed class KitProcesses(string schedulerExe, string poolFile, string? trayExitName = null, Func<string, IReadOnlyList<string>>? running = null) : IKitProcesses, IDisposable
{
    private readonly string _exe = Path.GetFullPath(schedulerExe);

    private readonly string _trayExitName = trayExitName ?? TrayExitSignal.NameFor(Path.GetDirectoryName(Path.GetFullPath(schedulerExe))!);

    private readonly Func<string, IReadOnlyList<string>> _running = running ?? RunningExes;

    private string ExeName => Path.GetFileNameWithoutExtension(_exe);

    /// <inheritdoc/>
    public bool SignalTrays()
    {
        if (!EventWaitHandle.TryOpenExisting(_trayExitName, out var handle))
        {
            return false;
        }

        // Not held: once this kit's last tray has closed it the event is gone, and that is how Trays() sees them gone.
        using (handle)
        {
            return handle.Set();
        }
    }

    /// <inheritdoc/>
    public void ResetTrays()
    {
        if (EventWaitHandle.TryOpenExisting(_trayExitName, out var handle))
        {
            using (handle)
            {
                handle.Reset();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>1 while any tray of this kit still holds its exit signal (how many cannot be seen), else 0.</remarks>
    public int Trays()
    {
        if (!EventWaitHandle.TryOpenExisting(_trayExitName, out var handle))
        {
            return 0;
        }

        handle.Dispose();
        return 1;
    }

    /// <inheritdoc/>
    public bool SignalPool()
    {
        if (!EventWaitHandle.TryOpenExisting(RepoShutdown.Name(poolFile), out var handle))
        {
            return false;
        }

        using (handle)
        {
            return handle.Set();
        }
    }

    /// <inheritdoc/>
    public int ExeProcesses() => _running(ExeName).Count(path => string.Equals(path, _exe, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public IReadOnlyList<string> OtherKits() =>
        [.. _running(ExeName)
            .Where(path => !string.Equals(path, _exe, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetDirectoryName(path) ?? path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing is held between calls (see SignalTrays); kept so a caller's using stays as it was.
    }

    // The full exe path of every running process of the name, this user's alone (another user's cannot be read).
    private static List<string> RunningExes(string name)
    {
        var paths = new List<string>();
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try
                {
                    if (p.Id != Environment.ProcessId && p.MainModule?.FileName is { } file)
                    {
                        paths.Add(Path.GetFullPath(file));
                    }
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // ended meanwhile, or another user's: not one of ours
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// The tray is a Windows program with no console: borrow the console of the command that started it, if it
    /// has one and its output is not already redirected, so the one line is seen there.
    /// </summary>
    public static void UseParentConsole()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // no console to borrow: the line goes nowhere, the exit code still says it
        }
    }

    private const uint AttachParentProcess = uint.MaxValue;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);
}
