using System.Diagnostics;

namespace Scheduler.Tray;

/// <summary>
/// The notification-area icon over one scheduler: double-click opens its page; the menu has the page,
/// the log window, Restart Switchyard, Exit leaving the workers running, and Exit (stop all) (670-B).
/// The output threads only fill the <see cref="LogBuffer"/>; a timer on the UI thread reads it, so
/// nothing is marshalled.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(3);

    /// <summary>How long the scheduler is given to end by itself after the Ctrl+C before it is ended outright.</summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(20);

    private readonly TrayArguments _args;
    private readonly string _exe;
    private readonly LogBuffer _log = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _restart;
    private readonly ToolStripMenuItem _update;
    private readonly ToolStripMenuItem _leave;
    private readonly ToolStripMenuItem _exit;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private SchedulerProcess? _process;
    private SchedulerState _state;
    private string _project;
    private Uri? _page;
    private readonly FirstPageOpen _firstOpen;
    private readonly CancellationTokenSource _closing = new();
    private long _scanned;
    private int? _workers;
    private DateTime _lastPoll = DateTime.MinValue;
    private bool _polling;
    private bool _busy;
    private LogForm? _logForm;
    private AttachedScheduler? _attached;
    private SchedulerLogTail? _tail;
    private IReadOnlyList<(string Name, string State)> _poolProjects = [];

    /// <summary>Starts the scheduler and shows the icon.</summary>
    public TrayApp(TrayArguments args, string exe)
    {
        _args = args;
        _exe = exe;
        _project = args.ProjectGuess();
        _firstOpen = new FirstPageOpen(args.Quiet, ProbeAsync, Browse, Task.Delay);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open page", null, (_, _) => OpenPage());
        menu.Items.Add("View log", null, (_, _) => ShowLog());
        _restart = new ToolStripMenuItem("Restart Switchyard", null, async (_, _) => await RestartAsync());
        menu.Items.Add(_restart);
        _update = new ToolStripMenuItem("Update and restart", null, async (_, _) => await UpdateAndRestartAsync());
        menu.Items.Add(_update);
        _leave = new ToolStripMenuItem("Exit, leave workers running", null, async (_, _) => await ExitAsync(stopWorkers: false));
        menu.Items.Add(_leave);
        menu.Items.Add(new ToolStripSeparator());
        _exit = new ToolStripMenuItem("Exit (stop all)", null, async (_, _) => await ExitAsync(stopWorkers: true));
        menu.Items.Add(_exit);
        _icon = new NotifyIcon
        {
            Icon = TrayIconLoader.Load(AppContext.BaseDirectory, _log.Add),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenPage();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_state == SchedulerState.Exited)
            {
                _ = RestartAsync();
            }
        };
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += async (_, _) => await TickAsync();

        // 975-B: a scheduler already running for this repository is attached to, not started again.
        // 1013-B: a pool running from the same pool file is attached to the same way, by its pool file's event;
        // starting a second one made two schedulers (MP-H1).
        _attached = args.Pool
            ? AttachedScheduler.TryAttachPool(AttachedScheduler.PoolFileOf(args, LocalAppData), exe, _log)
            : AttachedScheduler.TryAttach(args.Repository, exe, _log);
        if (_attached is null)
        {
            Start();
        }
        else
        {
            Attach(_attached);
        }

        _timer.Start();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _closing.Cancel();
            _closing.Dispose();
            _http.Dispose();
            _process?.Dispose();
            _attached?.Dispose();
            _logForm?.Dispose();
        }

        base.Dispose(disposing);
    }

    // The user's local application data folder, where the default pool file and the pool's log are (MP-C1).
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // 975-B: follow a scheduler this tray did not start: its log file instead of its console, and its
    // page from the start line in that file.
    private void Attach(AttachedScheduler attached)
    {
        // 1013-B: a pool's dated logs are in the logs folder of its log folder, the one with its status.txt.
        _tail = new SchedulerLogTail(_args.Pool
            ? Path.Combine(Path.GetDirectoryName(PoolStatusFile.PathFor(_args.PoolFile, LocalAppData)) ?? "", "logs")
            : SchedulerLogTail.FolderFor(_args.Repository));
        var (start, recent) = _tail.Open(200);
        foreach (var line in recent)
        {
            _log.Add(line);
        }

        _log.Add($"[tray] tray attached to the running scheduler pid {attached.PidText}; it was not started again");
        _scanned = _log.Total;
        if (StartLine.TryParse(start, out var project, out var page))
        {
            _project = project;
            _page = page;
            _ = _firstOpen.RunAsync(page!, _closing.Token);
        }

        _state = SchedulerState.Running;
        UpdateTooltip();
    }

    private void Start(string? replaces = null)
    {
        // A scheduler started here is this tray's own from now on, whichever it followed before.
        _attached?.Dispose();
        _attached = null;
        _tail = null;
        _process?.Dispose();
        _page = null;
        _workers = null;
        _scanned = _log.Total;
        _state = SchedulerState.Starting;
        _process = new SchedulerProcess(_exe, ArgsFor(replaces), _log);
        try
        {
            _process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Add($"[tray] could not start the scheduler: {e.Message}");
        }

        UpdateTooltip();
    }

    // The scheduler's arguments, plus the version it takes over from (967-B), which it writes into its
    // log so the two builds can be told apart afterwards. The scheduler leaves a switch it does not
    // bind alone, as it does -ShutdownEvent and -NoBrowser.
    private IReadOnlyList<string> ArgsFor(string? replaces) =>
        string.IsNullOrEmpty(replaces) ? _args.SchedulerArgs : [.. _args.SchedulerArgs, $"{ReplacesSwitch.Name}:{replaces}"];

    private async Task TickAsync()
    {
        if (_tail is not null)
        {
            foreach (var line in _tail.ReadNew())
            {
                _log.Add(line);
            }
        }

        ScanForStartLine();
        var ended = _attached is not null ? _attached.HasExited : _process is { HasExited: true };
        if (ended && _state is SchedulerState.Starting or SchedulerState.Running)
        {
            Ended();
            return;
        }

        if (_state == SchedulerState.Running && !_polling && DateTime.UtcNow - _lastPoll >= PollEvery)
        {
            _polling = true;
            _lastPoll = DateTime.UtcNow;
            try
            {
                _workers = LiveStatus.Workers(await GetAsync("live.json"));
                if (_args.Pool)
                {
                    _poolProjects = PoolStatusFile.Read(PoolStatusFile.PathFor(_args.PoolFile, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
                }
            }
            finally
            {
                _polling = false;
            }
        }

        UpdateTooltip();
    }

    private void ScanForStartLine()
    {
        if (_page is not null)
        {
            return;
        }

        var (lines, total) = _log.Since(_scanned);
        lines ??= _log.Snapshot().Lines;
        _scanned = total;
        foreach (var line in lines)
        {
            if (StartLine.TryParse(line, out var project, out var page))
            {
                _project = project;
                _page = page;
                _ = _firstOpen.RunAsync(page!, _closing.Token);
                if (_state == SchedulerState.Starting)
                {
                    _state = SchedulerState.Running;
                }

                return;
            }
        }
    }

    private void Ended()
    {
        _state = SchedulerState.Exited;
        _workers = null;
        var code = _process?.ExitCode is { } c ? $" (exit code {c})" : "";
        var last = LastSchedulerLine();
        _log.Add($"[tray] the scheduler ended by itself{code}");
        UpdateTooltip();
        _icon.ShowBalloonTip(
            10000,
            $"{_project} scheduler stopped{code}",
            (last.Length > 0 ? last + "\n" : "") + "Click here, or choose Restart in the icon's menu, to start it again.",
            ToolTipIcon.Warning);
    }

    private string LastSchedulerLine()
    {
        var lines = _log.Snapshot().Lines;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].StartsWith("[tray]", StringComparison.Ordinal) && lines[i].Trim().Length > 0)
            {
                return lines[i].Trim();
            }
        }

        return "";
    }

    /// <summary>
    /// End the scheduler and start it again from the same folder with the same arguments, so a scheduler
    /// that has just been built or published is the one that runs (670-B). Its workers are not touched:
    /// they are adopted by the new scheduler, as after a Ctrl+C. The log window keeps its lines; the
    /// restart is marked in it.
    /// </summary>
    private async Task RestartAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetMenuEnabled(false);
        if (_attached is { } attached)
        {
            // 975-B: the scheduler this tray attached to ends through its shutdown request, and the one
            // started after it is this tray's own.
            _log.Add("[tray] restarting the attached scheduler; its workers keep running and the new one adopts them");
            var stopped = await StopCurrentAsync().ConfigureAwait(false);
            _busy = false;
            SetMenuEnabled(true);
            if (stopped)
            {
                Start();
            }
            else
            {
                _state = SchedulerState.Exited;
                _log.Add($"[tray] the attached scheduler (pid {attached.PidText}) is still running; no new one was started. Restart again, or use Exit (stop all).");
                UpdateTooltip();
            }

            return;
        }

        var process = _process;
        try
        {
            if (process is not null && !process.HasExited)
            {
                _log.Add("[tray] restarting the scheduler; its workers keep running and the new one adopts them");
                await StopCurrentAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _busy = false;
            SetMenuEnabled(true);
        }

        // The new scheduler is started whatever happened to the old one - that is the whole point of
        // Restart - unless the old one is still there, where a second would only sit waiting for its mutex.
        if (process is null || process.HasExited)
        {
            Start();
        }
        else
        {
            _state = SchedulerState.Exited;
            _log.Add($"[tray] the old scheduler (pid {process.Pid}) is still running; no new one was started. Restart again, or use Exit (stop all).");
            UpdateTooltip();
        }
    }

    /// <summary>
    /// Publish main's code beside the running scheduler, swap it into the running folder and start it from
    /// the same path, so the firewall rule the page is bound to still matches (967-B). The workers are not
    /// touched: the new scheduler adopts them, as after a Restart. A build that will not start within 30 s
    /// is swapped back for the build it replaced, and the log window says which version replaced which.
    /// </summary>
    private async Task UpdateAndRestartAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetMenuEnabled(false);
        try
        {
            var layout = DeployLayout.ForSchedulerExe(_exe);
            var fs = new SwapFileSystem();
            var updater = new SchedulerUpdater(
                fs,
                new DotNetPublishTool(line => _log.Add($"[tray] {line}")),
                new ExecutableVersionReader(),
                line => _log.Add($"[tray] {line}"),
                ExeCalls.Running);   // 1021-B: never swap the exe from under a worker's lock or check-push call

            // The tray's own files only have to stay put when this tray is running from the scheduler's
            // folder; running from its copy in %LOCALAPPDATA% (967-B) leaves the folder free to swap whole.
            var keep = TraySelfCopy.SameDir(AppContext.BaseDirectory, layout.Current)
                ? TraySelfCopy.Names(fs, layout.Current)
                : [];
            await updater.UpdateAsync(layout, new RunningScheduler(this), keep, usePublished: false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.Add($"[tray] the update went wrong: {e.Message}");
        }
        finally
        {
            _busy = false;
            SetMenuEnabled(true);
        }
    }

    /// <summary>End the running scheduler the way Restart does, leaving its workers alone. True when it ended.</summary>
    private async Task<bool> StopCurrentAsync()
    {
        if (_attached is { } attached)
        {
            _state = SchedulerState.Restarting;
            UpdateTooltip();
            return await attached.StopAsync(StopGrace).ConfigureAwait(false);
        }

        var process = _process;
        if (process is null || process.HasExited)
        {
            return true;
        }

        _state = SchedulerState.Restarting;
        UpdateTooltip();
        try
        {
            await process.StopAsync(StopGrace).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.Add($"[tray] stopping the scheduler went wrong: {e.Message}");
        }

        return process.HasExited;
    }

    // Whether the scheduler the update started said its start line in time. Its own Started/Exited
    // detection does the work: the start line gives the page address, and an exit is read from the process.
    private async Task<bool> WaitStartedAsync(TimeSpan window, CancellationToken cancel)
    {
        var until = DateTime.UtcNow + window;
        while (!cancel.IsCancellationRequested && DateTime.UtcNow < until)
        {
            ScanForStartLine();
            if (_page is not null)
            {
                return true;
            }

            if (_process is null || _process.HasExited)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancel).ConfigureAwait(false);
        }

        return _page is not null;
    }

    private void SetMenuEnabled(bool enabled)
    {
        _restart.Enabled = enabled;
        _update.Enabled = enabled;
        _leave.Enabled = enabled;
        _exit.Enabled = enabled;
    }

    /// <summary>
    /// End the scheduler and close the tray. With <paramref name="stopWorkers"/> it is the page's "Stop all
    /// and exit"; without it (670-B) the scheduler alone ends, so a worker mid-card is left to finish and
    /// the next scheduler adopts it.
    /// </summary>
    private async Task ExitAsync(bool stopWorkers)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _state = stopWorkers ? SchedulerState.Stopping : SchedulerState.Leaving;
        SetMenuEnabled(false);
        UpdateTooltip();
        var process = _process;
        if (_attached is { } attached)
        {
            // 975-B: not this tray's process; its shutdown request, or the page's Stop all.
            if (stopWorkers)
            {
                var exit = new ExitSequence(PostStopAllAsync, () => attached.HasExited, attached.WaitForExitAsync, attached.Kill);
                _log.Add($"[tray] exit: {await exit.RunAsync()}");
            }
            else
            {
                _log.Add("[tray] exit: the workers are left running; the next scheduler adopts them");
                _log.Add($"[tray] exit: {(await attached.StopAsync(StopGrace) ? "the scheduler ended" : "the scheduler would not end")}");
            }
        }
        else if (process is not null)
        {
            if (stopWorkers)
            {
                var exit = new ExitSequence(
                    PostStopAllAsync,
                    () => process.HasExited,
                    process.WaitForExitAsync,
                    process.Kill);
                _log.Add($"[tray] exit: {await exit.RunAsync()}");
            }
            else
            {
                _log.Add("[tray] exit: the workers are left running; the next scheduler adopts them");
                _log.Add($"[tray] exit: {(await process.StopAsync(StopGrace) ? "the scheduler ended" : "the scheduler would not end")}");
            }
        }

        _timer.Stop();
        _icon.Visible = false;
        ExitThread();
    }

    /// <summary>
    /// The scheduler this tray is running, seen as the deploy sees it: end it, and start it again from the
    /// same folder waiting for its start line (967-B). It is the same stop and start Restart uses, so the
    /// workers survive it either way (TRAY-5).
    /// </summary>
    private sealed class RunningScheduler(TrayApp app) : ISchedulerController
    {
        /// <inheritdoc/>
        public Task<bool> StopAsync(CancellationToken cancel) => app.StopCurrentAsync();

        /// <inheritdoc/>
        public async Task<bool> StartAsync(string replaces, TimeSpan window, CancellationToken cancel)
        {
            app.Start(replaces);
            return await app.WaitStartedAsync(window, cancel).ConfigureAwait(false);
        }
    }

    private async Task<bool> PostStopAllAsync(CancellationToken cancel)
    {
        if (_page is null)
        {
            return false;
        }

        using var response = await _http.PostAsync(new Uri(_page, "stopall"), new StringContent(""), cancel);
        return response.IsSuccessStatusCode;
    }

    private async Task<string?> GetAsync(string path)
    {
        if (_page is null)
        {
            return null;
        }

        try
        {
            return await _http.GetStringAsync(new Uri(_page, path));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private void UpdateTooltip() =>
        _icon.Text = _args.Pool
            ? TrayTooltip.ForPool(_state, _workers, LastSchedulerLine(), _poolProjects)
            : TrayTooltip.For(_project, _state, _workers, LastSchedulerLine());

    private void OpenPage()
    {
        if (_page is null)
        {
            _icon.ShowBalloonTip(5000, $"{_project} scheduler", "The page's address is not known yet: the scheduler has not said it.", ToolTipIcon.Info);
            return;
        }

        Browse(_page);
    }

    private static void Browse(Uri page) =>
        Process.Start(new ProcessStartInfo(page.ToString()) { UseShellExecute = true })?.Dispose();

    // 1014-B: the page has answered when it gives any HTTP reply.
    private async Task<bool> ProbeAsync(Uri page, CancellationToken cancel)
    {
        try
        {
            using var response = await _http.GetAsync(page, cancel);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void ShowLog()
    {
        if (_logForm is { IsDisposed: false })
        {
            _logForm.WindowState = FormWindowState.Normal;
            _logForm.Activate();
            return;
        }

        _logForm = new LogForm(_log, $"{_project} scheduler - log");
        _logForm.Show();
    }
}
