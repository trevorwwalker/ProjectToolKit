// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using System.Text;

namespace Scheduler.Tray;

/// <summary>
/// <c>scheduler-next.exe</c>, started hidden with its console lines going into a <see cref="LogBuffer"/>.
/// No console window: the scheduler itself does not change (550-B). The lines are read as UTF-8, the
/// encoding the scheduler writes them in (670-B), whatever the console code page would have made them.
/// </summary>
public sealed class SchedulerProcess(string exePath, IReadOnlyList<string> args, LogBuffer log, string? shutdownEvent = null) : IDisposable
{
    /// <summary>The encoding of the scheduler's console lines; the scheduler's own <c>ConsoleOutput.Utf8</c>.</summary>
    public static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private const int KillWaitSeconds = 15;

    /// <summary>How long the scheduler is given after a Ctrl+C that was actually sent before it is ended outright.</summary>
    private const int CtrlCWaitSeconds = 10;

    // 670-B: how the tray asks the scheduler to end by itself. The scheduler is the only thing that must
    // stop, and a console Ctrl+C reaches every process sharing that console - the workers, which the card
    // asks to keep running, among them. So the tray owns a named event, hands the scheduler its name on
    // the command line, and sets it; the scheduler's loop sees it and returns by itself.
    private const string ShutdownSwitchName = "-ShutdownEvent";

    private Process? _process;
    private int? _pid;
    private volatile bool _ended;
    private EventWaitHandle? _shutdown;
    private string _shutdownName = "";

    /// <summary>Whether it has ended (or was never started).</summary>
    public bool HasExited => _process is null || _ended;

    /// <summary>Its process id, or null when it never started.</summary>
    public int? Pid => _pid;

    /// <summary>Starts it.</summary>
    public void Start()
    {
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = LogEncoding,
            StandardErrorEncoding = LogEncoding,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // A fresh event for every process, so the scheduler a Restart starts never sees the signal that
        // ended the one before it. It is created before the child, which opens the same name.
        _shutdown?.Dispose();
        _shutdownName = string.IsNullOrWhiteSpace(shutdownEvent)
            ? $"ProjectToolkit-{Guid.NewGuid():N}-scheduler-shutdown"
            : shutdownEvent.Trim();
        _shutdown = new EventWaitHandle(false, EventResetMode.ManualReset, _shutdownName);
        info.ArgumentList.Add($"{ShutdownSwitchName}:{_shutdownName}");

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Receive(e.Data);
        process.ErrorDataReceived += (_, e) => Receive(e.Data);
        process.Exited += (_, _) => _ended = true;
        _ended = false;
        _pid = null;
        _process = process;
        log.Add($"[tray] starting {exePath} {string.Join(' ', args)}");
        process.Start();
        _pid = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// End the scheduler and nothing else (670-B). First the clean shutdown path: the event on the
    /// scheduler's own command line, which the scheduler watches and ends by itself - it closes its page
    /// and writes its status file, and reaches nobody else. Then the Ctrl+C a developer would press, which
    /// only works when the tray has no console of its own. It is killed outright last, and even then it is
    /// its own process that is killed, never its job object, which would take the workers with them. The
    /// workers are in job objects of their own that the scheduler does not end, so all three paths leave
    /// them running for the next scheduler to adopt. True when it ended.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan grace)
    {
        if (_process is not { } process)
        {
            return true;
        }

        if (process.HasExited)
        {
            _ended = true;
            return true;
        }

        if (_shutdown is { } shutdown)
        {
            log.Add($"[tray] asking the scheduler (pid {_pid}) to end by itself; its workers keep running");
            shutdown.Set();
            if (await WaitOutAsync(grace).ConfigureAwait(false))
            {
                return true;
            }
        }

        log.Add($"[tray] Ctrl+C to the scheduler (pid {_pid}); its workers keep running");
        if (ConsoleSignal.TrySendCtrlC(_pid ?? process.Id) && await WaitOutAsync(TimeSpan.FromSeconds(CtrlCWaitSeconds)).ConfigureAwait(false))
        {
            return true;
        }

        log.Add("[tray] the scheduler did not end by itself; ending its process only, not its job");
        Kill();
        await WaitOutAsync(TimeSpan.FromSeconds(KillWaitSeconds)).ConfigureAwait(false);
        return process.HasExited;
    }

    /// <summary>Kills it, without its workers: their job objects end with it if they are bound to it.</summary>
    public void Kill()
    {
        try
        {
            if (_process is { HasExited: false } p)
            {
                p.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    /// <summary>Waits for it to end and for its last lines to be read.</summary>
    public async Task WaitForExitAsync(CancellationToken cancel)
    {
        if (_process is { } p)
        {
            await p.WaitForExitAsync(cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Its exit code, once it has ended.</summary>
    public int? ExitCode => _process is { HasExited: true } p ? p.ExitCode : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        _process?.Dispose();
        _shutdown?.Dispose();
        _shutdown = null;
    }

    private async Task<bool> WaitOutAsync(TimeSpan wait)
    {
        if (_process is not { } process)
        {
            return true;
        }

        using var cancel = new CancellationTokenSource(wait);
        try
        {
            await process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
            _ended = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private void Receive(string? line)
    {
        if (line is not null)
        {
            log.Add(line);
        }
    }
}
