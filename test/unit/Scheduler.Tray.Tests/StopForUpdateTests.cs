// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 1245-B, INS-19 and INS-22: <c>scheduler-tray.exe -StopForUpdate</c> ends the trays and the pool's scheduler
/// the way Restart does, waits for workers' lock and check-push calls from the exe, prints one line, and exits 0
/// when nothing of the kit is running any more, 1 otherwise.
/// </summary>
public class StopForUpdateTests
{
    private readonly FakeKit _kit = new();
    private readonly List<TimeSpan> _waits = [];

    private StopForUpdate Stop() => new(_kit, (span, _) =>
    {
        _waits.Add(span);
        _kit.Tick();
        return Task.CompletedTask;
    });

    private TimeSpan Waited => _waits.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    [Fact]
    public async Task Nothing_running_is_exit_0_at_once()
    {
        var (code, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.Equal("stop for update: nothing of the kit was running", line);
        Assert.Empty(_waits);
    }

    // The tray ends its scheduler as Exit, leave workers running does; the stop waits for both to be gone.
    [Fact]
    public async Task A_running_tray_is_asked_to_exit_and_its_scheduler_ends_with_it()
    {
        _kit.TrayListens = true;
        _kit.TraysLeft = [1, 1, 0];
        _kit.ExeLeft = [1, 1, 1, 0];

        var (code, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.Contains("the tray was asked to exit", line, StringComparison.Ordinal);
        Assert.Contains("the workers keep running", line, StringComparison.Ordinal);
        Assert.True(_kit.TraysSignalled);
        Assert.True(_kit.TraysReset);
    }

    // A pool started without a tray: its shutdown event, which the scheduler answers as it does Restart.
    [Fact]
    public async Task A_pool_with_no_tray_is_asked_to_end_by_its_shutdown_event()
    {
        _kit.PoolRuns = true;
        _kit.ExeLeft = [1, 0];

        var (code, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.Contains("the pool's scheduler was asked to end", line, StringComparison.Ordinal);
        Assert.DoesNotContain("tray", line, StringComparison.Ordinal);
    }

    // 1021-B's wait: a worker's lock call still running from the exe holds the stop until it ends.
    [Fact]
    public async Task A_lock_call_still_running_is_waited_for()
    {
        _kit.ExeLeft = [1, 1, 1, 1, 1, 0];

        var (code, _) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.Equal(5 * StopForUpdate.Poll, Waited);
    }

    [Fact]
    public async Task A_call_that_outlives_the_window_is_exit_1_and_says_so()
    {
        _kit.ExeLeft = [1];

        var (code, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.NotStopped, code);
        Assert.Equal(SchedulerUpdater.CallsWindow, Waited);
        Assert.Contains("1 process is still running from scheduler-next.exe after 150 s", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tray_that_will_not_exit_is_exit_1_and_the_request_is_taken_back()
    {
        _kit.TrayListens = true;
        _kit.TraysLeft = [2];

        var (code, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.NotStopped, code);
        Assert.Equal(StopForUpdate.TraysWindow, Waited);
        Assert.Contains("2 trays are still running", line, StringComparison.Ordinal);
        Assert.True(_kit.TraysReset);
    }

    [Fact]
    public async Task The_answer_is_one_line()
    {
        _kit.TrayListens = true;
        _kit.PoolRuns = true;
        _kit.ExeLeft = [3];

        var (_, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.DoesNotContain('\n', line);
    }

    [Theory]
    [InlineData(new[] { "-StopForUpdate" }, "")]
    [InlineData(new[] { "-stopforupdate" }, "")]
    [InlineData(new[] { "-StopForUpdate", @"C:\pools\pool.json" }, @"C:\pools\pool.json")]
    public void The_switch_is_read_with_an_optional_pool_file(string[] given, string pool)
    {
        var args = TrayArguments.Parse(given);

        Assert.True(args.Ok);
        Assert.True(args.StopForUpdate);
        Assert.False(args.Pool);
        Assert.Equal(pool, args.PoolFile);
        Assert.Empty(args.SchedulerArgs);
    }

    [Fact]
    public void The_switch_keeps_the_scheduler_folder_it_is_given()
    {
        var args = TrayArguments.Parse(["-StopForUpdate", @"-SchedulerDir:C:\kit\out\windows\scheduler"]);

        Assert.True(args.StopForUpdate);
        Assert.Equal(@"C:\kit\out\windows\scheduler", args.SchedulerDir);
        Assert.Equal("", args.PoolFile);
    }

    [Fact]
    public void A_repository_start_is_not_a_stop() =>
        Assert.False(TrayArguments.Parse([@"C:\Projects\Game"]).StopForUpdate);

    // The tray's own exit signal, under a name of the test's own so no real tray on the machine sees it.
    [Fact]
    public void The_tray_sees_the_exit_request_and_it_is_taken_back_after()
    {
        var name = $"ProjectToolkit-test-{Guid.NewGuid():N}";
        using var tray = TrayExitSignal.Listen(name);
        using var kit = new KitProcesses(@"C:\nowhere\scheduler-next.exe", Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"), name);

        Assert.False(tray.IsSet);
        Assert.True(kit.SignalTrays());
        Assert.True(tray.IsSet);
        kit.ResetTrays();
        Assert.False(tray.IsSet);
    }

    [Fact]
    public void No_tray_listening_and_no_pool_running_is_nothing_to_signal()
    {
        using var kit = new KitProcesses(@"C:\nowhere\scheduler-next.exe", Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"), $"ProjectToolkit-test-{Guid.NewGuid():N}");

        Assert.False(kit.SignalTrays());
        Assert.False(kit.SignalPool());
        Assert.Equal(0, kit.ExeProcesses());
    }

    // The pool's shutdown event is the one its scheduler listens on (RepoShutdown keyed by the pool file).
    [Fact]
    public void A_running_pool_gets_its_shutdown_event_set()
    {
        var pool = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        using var scheduler = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(pool));
        using var kit = new KitProcesses(@"C:\nowhere\scheduler-next.exe", pool, $"ProjectToolkit-test-{Guid.NewGuid():N}");

        Assert.True(kit.SignalPool());
        Assert.True(scheduler.WaitOne(0));
    }

    // 1258-B: the tray's exit signal is named by its kit folder, the same however the folder is written.
    [Fact]
    public void Each_kit_folder_has_its_own_tray_exit_signal()
    {
        var dev = TrayExitSignal.NameFor(@"C:\Projects\ProjectToolkit\out\windows\scheduler");
        var installed = TrayExitSignal.NameFor(@"C:\Users\me\AppData\Local\Programs\ProjectToolkit\out\windows\scheduler");

        Assert.NotEqual(dev, installed);
        Assert.StartsWith(TrayExitSignal.Prefix, dev, StringComparison.Ordinal);
        Assert.Equal(dev, TrayExitSignal.NameFor(@"c:\projects\projecttoolkit\out\windows\scheduler\"));
    }

    // 1258-B: two kit folders, a tray and a scheduler running from each. The stop run from A asks A's tray to exit
    // and waits for A's scheduler; B's tray never sees the request, the pool (which could be B's) is not asked to
    // end, and B is named in the line as left alone, which is not a failure.
    [Fact]
    public async Task A_stop_from_one_kit_folder_leaves_another_folders_switchyard_alone()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ptk-kits-{Guid.NewGuid():N}");
        var a = Path.Combine(root, "A", "out", "windows", "scheduler");
        var b = Path.Combine(root, "B", "out", "windows", "scheduler");
        var exeA = Path.Combine(a, "scheduler-next.exe");
        var exeB = Path.Combine(b, "scheduler-next.exe");
        var running = new List<string> { exeA, exeB };
        var pool = Path.Combine(root, "pool.json");
        using var poolEvent = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(pool));
        using var trayB = TrayExitSignal.Listen(TrayExitSignal.NameFor(b));
        var trayA = TrayExitSignal.Listen(TrayExitSignal.NameFor(a));
        var trayAGone = false;
        using var kit = new KitProcesses(exeA, pool, running: name => name == "scheduler-next" ? [.. running] : []);

        // A's tray exits when asked, and ends its scheduler as "Exit, leave workers running" does.
        var stop = new StopForUpdate(kit, (_, _) =>
        {
            if (!trayAGone && trayA.IsSet)
            {
                trayA.Dispose();
                trayAGone = true;
                running.Remove(exeA);
            }

            return Task.CompletedTask;
        });

        var (code, line) = await stop.RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.True(trayAGone);
        Assert.False(trayB.IsSet);
        Assert.False(poolEvent.WaitOne(0));
        Assert.Contains("the tray was asked to exit", line, StringComparison.Ordinal);
        Assert.Contains($"another Switchyard runs from {b}; left alone", line, StringComparison.Ordinal);
        Assert.Equal([exeB], running);
    }

    [Fact]
    public void A_kit_counts_only_the_schedulers_and_trays_of_its_own_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ptk-kits-{Guid.NewGuid():N}");
        var a = Path.Combine(root, "A");
        var b = Path.Combine(root, "B");
        string[] running = [Path.Combine(a, "scheduler-next.exe"), Path.Combine(a, "scheduler-next.exe"), Path.Combine(b, "scheduler-next.exe")];
        using var kitA = new KitProcesses(Path.Combine(a, "scheduler-next.exe"), Path.Combine(root, "pool.json"), running: _ => running);

        Assert.Equal(2, kitA.ExeProcesses());
        Assert.Equal([b], kitA.OtherKits());
        Assert.Equal(0, kitA.Trays());
        using (TrayExitSignal.Listen(TrayExitSignal.NameFor(b)))
        {
            Assert.Equal(0, kitA.Trays());
            Assert.False(kitA.SignalTrays());
        }

        using (TrayExitSignal.Listen(TrayExitSignal.NameFor(a)))
        {
            Assert.Equal(1, kitA.Trays());
        }

        Assert.Equal(0, kitA.Trays());
    }

    // 1258-B: the one pool's shutdown event is shared by every folder's Switchyard, so it is set only when a
    // scheduler runs from this folder and none from another.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task The_pool_is_asked_to_end_only_when_it_can_be_this_kits(int schedulers, bool asked)
    {
        _kit.PoolRuns = true;
        _kit.ExeLeft = [schedulers, 0];

        var (code, _) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.Equal(asked, _kit.PoolSignalled);
    }

    [Fact]
    public async Task Another_folders_switchyard_keeps_its_pool_and_is_said_in_the_line()
    {
        _kit.PoolRuns = true;
        _kit.Others = [@"C:\Projects\ProjectToolkit\out\windows\scheduler"];

        var (code, line) = await Stop().RunAsync(CancellationToken.None);

        Assert.Equal(StopForUpdate.Stopped, code);
        Assert.False(_kit.PoolSignalled);
        Assert.Equal(@"stop for update: nothing of the kit was running; another Switchyard runs from C:\Projects\ProjectToolkit\out\windows\scheduler; left alone", line);
    }

    // 1258-B: bin\test-installer.ps1 refuses to start while a Switchyard runs (a renamed copy of ping stands in for
    // one), before it installs anything.
    [Fact]
    public async Task The_installer_test_refuses_while_another_switchyard_runs()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ptk-fake-kit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var fake = Path.Combine(dir, "scheduler-next.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), fake);
        var setup = Path.Combine(dir, "setup.exe");
        File.WriteAllText(setup, "not an installer");
        using var running = Process.Start(new ProcessStartInfo(fake, "-n 120 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        try
        {
            var start = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot(), "bin", "test-installer.ps1"), "-Setup", setup })
            {
                start.ArgumentList.Add(arg);
            }

            using var p = Process.Start(start)!;
            var stderr = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(60_000), "test-installer.ps1 did not finish in 60 s");

            Assert.Equal(1, p.ExitCode);
            Assert.Contains("test-installer: refused: a Switchyard is running", output, StringComparison.Ordinal);
            Assert.Contains("-AllowRunningSwitchyard", output, StringComparison.Ordinal);
            Assert.Equal("", await stderr);
        }
        finally
        {
            running.Kill();
            running.WaitForExit(10_000);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "bin", "projecttoolkit.ps1")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("bin/projecttoolkit.ps1 not found above the test's folder");
    }

    // Counts given per look; the last value holds once the list runs out. Tick moves to the next look.
    private sealed class FakeKit : IKitProcesses
    {
        private int _look;

        public bool TrayListens { get; set; }

        public bool PoolRuns { get; set; }

        public int[] TraysLeft { get; set; } = [0];

        public int[] ExeLeft { get; set; } = [0];

        public bool TraysSignalled { get; private set; }

        public bool TraysReset { get; private set; }

        public void Tick() => _look++;

        public bool SignalTrays()
        {
            TraysSignalled = TrayListens;
            return TrayListens;
        }

        public void ResetTrays() => TraysReset = true;

        public int Trays() => At(TraysLeft);

        public bool PoolSignalled { get; private set; }

        public IReadOnlyList<string> Others { get; set; } = [];

        public bool SignalPool()
        {
            PoolSignalled = PoolRuns;
            return PoolRuns;
        }

        public int ExeProcesses() => At(ExeLeft);

        public IReadOnlyList<string> OtherKits() => Others;

        private int At(int[] counts) => counts[Math.Min(_look, counts.Length - 1)];
    }
}
