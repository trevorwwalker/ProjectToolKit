namespace Scheduler.Tray;

/// <summary>
/// "Update and restart" (967-B): publish a new build beside the running scheduler, swap the folders, and
/// start the new one from the same path so the page's firewall rule (BUILD-3) still matches. Nothing here
/// touches the workers - the scheduler ends the way Restart ends it (TRAY-5, TRAY-8) and the new one
/// adopts them. A new build that will not start within <see cref="StartWindow"/> is swapped back for the
/// build it replaced, which is started again, and the log window says so.
/// </summary>
public sealed class SchedulerUpdater(
    ISwapFileSystem fs,
    IPublishTool publish,
    IVersionReader versions,
    Action<string> log,
    Func<string, int>? exeCalls = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>How long the new scheduler is given to say it is up before it is swapped back (967-B).</summary>
    public static readonly TimeSpan StartWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 1021-B: how long the swap waits for a worker's <c>lock</c> or <c>check-push</c> call still running from
    /// the exe (a lock call waits up to 110 s for its answer).
    /// </summary>
    public static readonly TimeSpan CallsWindow = TimeSpan.FromSeconds(150);

    /// <summary>How often the swap looks again while such a call runs.</summary>
    public static readonly TimeSpan CallsPoll = TimeSpan.FromSeconds(2);

    private readonly List<string> _log = [];

    /// <summary>The lines the run wrote, for a test or a log window.</summary>
    public IReadOnlyList<string> Lines => _log;

    /// <summary>
    /// Deploy the build in <c>scheduler.next</c> (publishing one first unless <paramref name="usePublished"/>
    /// says one is already there) and put it in the running folder.
    /// <paramref name="keepInCurrent"/> names the files of the running folder that must not move - the
    /// tray's own exe and dll when the tray is running from that folder.
    /// </summary>
    public async Task<UpdateOutcome> UpdateAsync(
        DeployLayout layout,
        ISchedulerController control,
        IReadOnlyList<string> keepInCurrent,
        bool usePublished,
        CancellationToken cancel)
    {
        var oldVersion = versions.Read(layout.SchedulerExe);
        Say($"update: the scheduler running from {layout.Current} is {Shown(oldVersion)}");

        if (!usePublished)
        {
            if (!fs.FileExists(layout.HostProject + ".csproj"))
            {
                return Fail($"update: there is no project to publish at {layout.HostProject}", oldVersion, "");
            }

            fs.Delete(layout.Next);
            Say($"update: publishing {layout.HostProject} into {layout.Next}; the running scheduler is left alone");
            if (!await publish.PublishAsync(layout.HostProject, layout.Next, cancel).ConfigureAwait(false))
            {
                return Fail("update: the publish did not finish; the running scheduler was not touched", oldVersion, "");
            }
        }
        else
        {
            Say($"update: using the build already published in {layout.Next}");
        }

        var newVersion = versions.Read(layout.NextSchedulerExe);
        if (!fs.FileExists(layout.NextSchedulerExe))
        {
            return Fail("update: the published folder has no scheduler in it", oldVersion, newVersion);
        }

        Say("update: ending the scheduler; its workers keep running and the new one adopts them");
        if (!await control.StopAsync(cancel).ConfigureAwait(false))
        {
            return Fail("update: the running scheduler would not end; nothing was swapped", oldVersion, newVersion);
        }

        if (!await CallsEndedAsync(layout.SchedulerExe, cancel).ConfigureAwait(false))
        {
            var message = $"update: a lock or check-push call still runs from {layout.SchedulerExe} after {(int)CallsWindow.TotalSeconds} s; nothing was swapped";
            Say(message);
            await RestartOldAsync(control, layout, oldVersion, cancel).ConfigureAwait(false);
            return new(UpdateResult.Failed, message, oldVersion, newVersion);
        }

        var swap = SchedulerSwap.Run(fs, layout, keepInCurrent);
        Say($"update: {swap.Message}");
        if (!swap.Ok)
        {
            await RestartOldAsync(control, layout, oldVersion, cancel).ConfigureAwait(false);
            return new(UpdateResult.Failed, $"update: {swap.Message}", oldVersion, newVersion);
        }

        if (await control.StartAsync(oldVersion, StartWindow, cancel).ConfigureAwait(false))
        {
            var done = new UpdateOutcome(UpdateResult.Deployed, "", oldVersion, newVersion);
            Say($"update: {done.Summary}");
            return done with { Message = done.Summary };
        }

        Say($"update: the new build {Shown(newVersion)} did not start within {(int)StartWindow.TotalSeconds} s; putting back {Shown(oldVersion)}");
        await control.StopAsync(cancel).ConfigureAwait(false);
        var back = SchedulerSwap.RollBack(fs, layout, keepInCurrent);
        Say($"update: {back.Message}");
        var started = await RestartOldAsync(control, layout, oldVersion, cancel).ConfigureAwait(false);
        return new(
            back.Ok && started ? UpdateResult.RolledBack : UpdateResult.Failed,
            back.Ok
                ? $"update: {new UpdateOutcome(UpdateResult.RolledBack, "", oldVersion, newVersion).Summary}"
                : "update: the new build would not start and the old one could not be put back",
            oldVersion,
            newVersion);
    }

    private async Task<bool> RestartOldAsync(ISchedulerController control, DeployLayout layout, string oldVersion, CancellationToken cancel)
    {
        if (await control.StartAsync("", StartWindow, cancel).ConfigureAwait(false))
        {
            Say($"update: the scheduler in {layout.Current} is running again");
            return true;
        }

        Say($"update: the scheduler in {layout.Current} did not start; start it from the icon's menu or by hand");
        return false;
    }

    // 1021-B: the swap must not replace the exe while a worker's lock or check-push call runs from it; it
    // waits for them as it waits for the scheduler to end, and gives up after CallsWindow.
    private async Task<bool> CallsEndedAsync(string exe, CancellationToken cancel)
    {
        if (exeCalls is null)
        {
            return true;
        }

        var wait = delay ?? Task.Delay;
        var waited = TimeSpan.Zero;
        var said = false;
        while (exeCalls(exe) > 0)
        {
            if (waited >= CallsWindow)
            {
                return false;
            }

            if (!said)
            {
                Say("update: a worker's lock or check-push call is running from the scheduler's exe; waiting for it to end");
                said = true;
            }

            await wait(CallsPoll, cancel).ConfigureAwait(false);
            waited += CallsPoll;
        }

        return true;
    }

    private UpdateOutcome Fail(string message, string oldVersion, string newVersion)
    {
        Say(message);
        return new(UpdateResult.Failed, message, oldVersion, newVersion);
    }

    private void Say(string line)
    {
        _log.Add(line);
        log(line);
    }

    private static string Shown(string version) => version is { Length: > 0 } v ? v : "an unknown version";
}
