using System.Diagnostics;

namespace Scheduler.Tray;

/// <summary>
/// A scheduler this tray found running for its repository instead of starting one (975-B). The tray does
/// not own its process, so it asks it to end through the repository's shutdown event (<see cref="RepoShutdown"/>),
/// which the scheduler watches as it does the event a tray hands it (670-B): it ends by itself and its
/// workers keep running. Its process is found by its path, to tell when it has ended; when it cannot be
/// told apart from another scheduler run from the same folder, its pid is not known.
/// </summary>
public sealed class AttachedScheduler : IDisposable
{
    private readonly EventWaitHandle _shutdown;
    private readonly Process? _process;
    private readonly LogBuffer _log;

    private AttachedScheduler(EventWaitHandle shutdown, Process? process, LogBuffer log)
    {
        _shutdown = shutdown;
        _process = process;
        _log = log;
    }

    /// <summary>Its process id, or null when it is not known.</summary>
    public int? Pid => _process?.Id;

    /// <summary>Whether it has ended, as far as can be told.</summary>
    public bool HasExited
    {
        get
        {
            try
            {
                return _process?.HasExited ?? false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// The scheduler running for <paramref name="repo"/>, or null when none is: its shutdown event exists only
    /// while it runs. <paramref name="exe"/> is the scheduler's path, used to find its process.
    /// </summary>
    public static AttachedScheduler? TryAttach(string repo, string exe, LogBuffer log)
    {
        if (!EventWaitHandle.TryOpenExisting(RepoShutdown.Name(repo), out var shutdown))
        {
            return null;
        }

        return new AttachedScheduler(shutdown, FindProcess(exe), log);
    }

    /// <summary>
    /// 1013-B: the pool running from <paramref name="poolFile"/>, or null when none is. A pool's shutdown event is
    /// named as a repository's is, keyed by its pool file's path (Scheduler.Host's <c>PoolComposition.ShutdownName</c>),
    /// so a tray started while a pool runs attaches to it instead of starting a second scheduler.
    /// </summary>
    public static AttachedScheduler? TryAttachPool(string poolFile, string exe, LogBuffer log) => TryAttach(poolFile, exe, log);

    /// <summary>1013-B: the pool file a <c>-Pool</c> tray means: the one given, else the default (MP-C1).</summary>
    public static string PoolFileOf(TrayArguments args, string localAppData) =>
        args.PoolFile.Length > 0 ? args.PoolFile : PoolStatusFile.DefaultPoolFile(localAppData);

    /// <summary>
    /// Ask it to end by itself and wait up to <paramref name="grace"/>. True when it ended, or - its process
    /// not being known - when the request was made and the wait is over.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan grace)
    {
        _log.Add($"[tray] asking the attached scheduler (pid {PidText}) to end by itself; its workers keep running");
        _shutdown.Set();
        if (_process is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return true;
        }

        using var cancel = new CancellationTokenSource(grace);
        try
        {
            await _process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Not within the grace; said below.
        }

        var ended = HasExited;
        if (ended)
        {
            // Clear for the next scheduler, which also clears it when it starts.
            _shutdown.Reset();
        }

        return ended;
    }

    /// <summary>Wait for it to end.</summary>
    public async Task WaitForExitAsync(CancellationToken cancel)
    {
        if (_process is not null)
        {
            await _process.WaitForExitAsync(cancel).ConfigureAwait(false);
        }
    }

    /// <summary>End its process outright (its workers' jobs are their own), the last step of Exit (stop all).</summary>
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

    /// <summary>The pid as it is written in the log.</summary>
    public string PidText => Pid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "not known";

    /// <inheritdoc/>
    public void Dispose()
    {
        _process?.Dispose();
        _shutdown.Dispose();
    }

    private static Process? FindProcess(string exe)
    {
        Process? found = null;
        var count = 0;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            string? path;
            try
            {
                path = p.MainModule?.FileName;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                path = null;
            }

            if (path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
            {
                count++;
                found?.Dispose();
                found = p;
            }
            else
            {
                p.Dispose();
            }
        }

        if (count == 1)
        {
            return found;
        }

        found?.Dispose();
        return null;
    }
}
