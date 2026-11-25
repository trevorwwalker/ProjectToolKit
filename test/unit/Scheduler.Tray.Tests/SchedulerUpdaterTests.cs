// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Tray.Tests.Fakes;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 967-B: "Update and restart" end to end with a fake file system and fake processes - publish beside
/// the running scheduler, end it the way Restart does, swap, and start the new one from the same path; and
/// the rollback when the new build never says it is up. The log lines name which version replaced which
/// (VER rules).
/// </summary>
public class SchedulerUpdaterTests
{
    private const string Out = @"C:\kit\out\windows";
    private readonly FakeSwapFileSystem _fs = new();
    private readonly DeployLayout _layout = new(Out);
    private readonly FakePublishTool _publish;
    private readonly FakeVersionReader _versions = new();
    private readonly FakeSchedulerController _control = new();
    private readonly List<string> _lines = [];
    private readonly SchedulerUpdater _updater;

    public SchedulerUpdaterTests()
    {
        _fs.WithFile(At(_layout.Current, "scheduler-next.exe"), "old exe")
            .WithFile(At(_layout.Current, "Scheduler.Host.dll"), "old host")
            .WithFile(At(_layout.Current, "scheduler-tray.exe"), "old tray")
            .WithFile(_layout.HostProjectFile, "<Project/>");
        _versions.ByFolder["scheduler"] = "3.0.7";
        _versions.ByFolder["scheduler.next"] = "3.0.8";
        _publish = new FakePublishTool(_fs);
        _updater = new SchedulerUpdater(_fs, _publish, _versions, line => _lines.Add(line));
    }

    // 967-B: one step - publish into scheduler.next while the old scheduler runs, end it, swap, start the
    // new one from the same path; and the log says 3.0.8 replaced 3.0.7.
    [Fact]
    public async Task A_deploy_publishes_swaps_and_starts_the_new_build()
    {
        var outcome = await _updater.UpdateAsync(_layout, _control, ["scheduler-tray.exe"], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Deployed, outcome.Result);
        Assert.Equal("3.0.7", outcome.OldVersion);
        Assert.Equal("3.0.8", outcome.NewVersion);
        Assert.Equal("scheduler 3.0.8 replaced 3.0.7", outcome.Summary);
        Assert.Equal(1, _publish.Runs);
        Assert.Equal(_layout.HostProject, _publish.Project);
        Assert.Equal(_layout.Next, _publish.OutputDir);
        Assert.Equal(1, _control.StopCount);
        Assert.Equal(["3.0.7"], _control.Replaces);
        Assert.Equal([SchedulerUpdater.StartWindow], _control.Windows);
        Assert.Equal("published scheduler-next.exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal("old exe", _fs.Content(At(_layout.Prev, "scheduler-next.exe")));
    }

    // The scheduler the new one replaces is passed on so its own log can say so too (967-B).
    [Fact]
    public async Task The_new_scheduler_is_told_which_version_it_took_over_from()
    {
        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Contains(_lines, line => line.Contains("3.0.8 replaced 3.0.7", StringComparison.Ordinal));
    }

    // A scheduler.next the developer published by hand is used as it stands (967-B).
    [Fact]
    public async Task An_already_published_build_is_deployed_without_building()
    {
        _fs.WithFile(At(_layout.Next, "scheduler-next.exe"), "hand published exe");

        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: true, CancellationToken.None);

        Assert.Equal(UpdateResult.Deployed, outcome.Result);
        Assert.Equal(0, _publish.Runs);
        Assert.Equal("hand published exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
    }

    // A publish that does not finish must leave the running scheduler exactly as it was - the whole point of
    // publishing beside it rather than into it.
    [Fact]
    public async Task A_publish_that_fails_touches_nothing()
    {
        _publish.Ok = false;

        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Equal(0, _control.StopCount);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.False(_fs.DirectoryExists(_layout.Prev));
    }

    // A scheduler that will not end means no swap: two of them would only fight over the mutex.
    [Fact]
    public async Task A_scheduler_that_will_not_end_is_not_swapped()
    {
        _control.Stops = false;

        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Contains("would not end", outcome.Message, StringComparison.Ordinal);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal("published scheduler-next.exe", _fs.Content(At(_layout.Next, "scheduler-next.exe")));
    }

    // 967-B: the new build never said its start line within 30 s, so the build it replaced goes back and
    // runs again, and the log says so.
    [Fact]
    public async Task A_build_that_will_not_start_is_swapped_back()
    {
        _control.StartsWithin = start => start != 1;

        var outcome = await _updater.UpdateAsync(_layout, _control, ["scheduler-tray.exe"], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.RolledBack, outcome.Result);
        Assert.Equal("the new scheduler 3.0.8 would not start; 3.0.7 is running again", outcome.Summary);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal("old host", _fs.Content(At(_layout.Current, "Scheduler.Host.dll")));
        Assert.False(_fs.DirectoryExists(_layout.Prev));
        Assert.Equal(["3.0.7", ""], _control.Replaces);
        Assert.Contains(_lines, line => line.Contains("did not start within 30 s", StringComparison.Ordinal));
    }

    // The swap itself can fail on a file Windows will not release; the scheduler that was running is started
    // again rather than left dead.
    [Fact]
    public async Task A_swap_that_fails_leaves_the_old_scheduler_running()
    {
        _control.OnStop = () => _fs.Lock(At(_layout.Current, "Scheduler.Host.dll"));

        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Equal([""], _control.Replaces);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
    }

    // Nothing published and no scheduler.next: the update says so instead of stopping a working scheduler.
    [Fact]
    public async Task Nothing_published_means_nothing_is_stopped()
    {
        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: true, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Equal(0, _control.StopCount);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
    }

    // The deploy needs a project to publish; a layout that is not a kit's says so rather than failing later.
    [Fact]
    public async Task Without_a_project_to_publish_the_scheduler_is_left_alone()
    {
        _fs.Delete(_layout.HostProjectFile);

        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Equal(0, _publish.Runs);
        Assert.Equal(0, _control.StopCount);
    }

    // 1263-B: a project file at the old wrong path (<kit>\code\src\Scheduler.Host.csproj) is not a project
    // to publish; the deploy still says so rather than publishing a folder with no project in it.
    [Fact]
    public async Task Update_ProjectFileOnlyAtTheOldWrongPath_Fails()
    {
        _fs.Delete(_layout.HostProjectFile);
        _fs.WithFile(At(_layout.KitRoot, @"code\src\Scheduler.Host.csproj"), "<Project/>");

        var outcome = await _updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Contains("there is no project to publish", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(0, _publish.Runs);
        Assert.Equal(0, _control.StopCount);
    }

    // 1021-B: a worker's lock or check-push call running from the exe holds the swap back until it ends.
    [Fact]
    public async Task The_swap_waits_for_a_lock_call_running_from_the_exe()
    {
        var calls = 2;
        var asked = new List<string>();
        var waits = new List<TimeSpan>();
        var updater = new SchedulerUpdater(_fs, _publish, _versions, line => _lines.Add(line),
            exe => { asked.Add(exe); return calls-- > 0 ? 1 : 0; },
            (t, _) => { waits.Add(t); return Task.CompletedTask; });

        var outcome = await updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Deployed, outcome.Result);
        Assert.All(asked, exe => Assert.Equal(_layout.SchedulerExe, exe));
        Assert.Equal([SchedulerUpdater.CallsPoll, SchedulerUpdater.CallsPoll], waits);
        Assert.Contains(_lines, line => line.Contains("lock or check-push call is running", StringComparison.Ordinal));
        Assert.Equal("published scheduler-next.exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
    }

    // 1021-B: a call that outlasts the window leaves the running build in place and starts it again.
    [Fact]
    public async Task A_call_that_never_ends_leaves_the_old_build_running()
    {
        var updater = new SchedulerUpdater(_fs, _publish, _versions, line => _lines.Add(line),
            _ => 1, (_, _) => Task.CompletedTask);

        var outcome = await updater.UpdateAsync(_layout, _control, [], usePublished: false, CancellationToken.None);

        Assert.Equal(UpdateResult.Failed, outcome.Result);
        Assert.Contains("nothing was swapped", outcome.Message, StringComparison.Ordinal);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal([SchedulerUpdater.StartWindow], _control.Windows);
    }

    private static string At(string dir, string name) => Path.Combine(dir, name);
}
