// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;

namespace Scheduler.Tray.Tests;

/// <summary>
/// The two things the tray does to the console of the process it started (670-B), tried against real
/// processes: read its lines as UTF-8, and end it the way Ctrl+C does so its own child survives.
/// </summary>
public sealed class SchedulerProcessTests : IDisposable
{
    private readonly string _dir = Scheduler.TestSupport.TestTmp.NewPath("670");

    public SchedulerProcessTests() => Directory.CreateDirectory(_dir);

    // The scheduler writes its console in UTF-8 (the Host's ConsoleOutput); the tray must read it back as
    // UTF-8 whatever this PC's console code page is, so a card title with accents or CJK reaches the log
    // window whole. The child here writes the bytes itself, so only the reader is under test.
    [Fact]
    public async Task A_non_ASCII_line_comes_back_as_the_scheduler_wrote_it()
    {
        const string Text = "Grüße 世界 café ☕ done";
        var data = Path.Combine(_dir, "line.txt");
        var script = Path.Combine(_dir, "write.ps1");
        File.WriteAllBytes(data, [.. SchedulerProcess.LogEncoding.GetBytes(Text + "\n")]);
        File.WriteAllText(script,
            "$b = [IO.File]::ReadAllBytes('" + data + "')\r\n" +
            "$o = [Console]::OpenStandardOutput()\r\n" +
            "$o.Write($b, 0, $b.Length)\r\n" +
            "$o.Flush()\r\n");

        var log = new LogBuffer();
        using var process = new SchedulerProcess("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script], log);
        process.Start();

        Assert.True(await WaitUntil(() => log.Snapshot().Lines.Any(l => l.Contains("done", StringComparison.Ordinal))), "the line never arrived");
        Assert.Equal(Text, log.Snapshot().Lines.Single(l => l.Contains("done", StringComparison.Ordinal)));
    }

    // 670-B: Restart and "Exit, leave workers running" end the scheduler and nothing else, so a worker
    // mid-card carries on and the next scheduler adopts it. The fake scheduler is a console program with a
    // child of its own, which is what a worker is to the scheduler.
    [Fact]
    public async Task Stop_ends_the_scheduler_and_leaves_its_child_running()
    {
        var ids = Path.Combine(_dir, "child.txt");
        var script = Path.Combine(_dir, "fake-scheduler.ps1");
        File.WriteAllText(script,
            "$c = Start-Process ping -ArgumentList '-n','120','127.0.0.1' -PassThru -WindowStyle Hidden\r\n" +
            $"Set-Content -Path '{_dir}\\child.tmp' -Value $c.Id\r\n" +
            $"Move-Item '{_dir}\\child.tmp' '{ids}'\r\n" +
            "Start-Sleep 120\r\n");

        var log = new LogBuffer();
        using var process = new SchedulerProcess("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script], log);
        process.Start();
        Assert.NotNull(process.Pid);
        Assert.True(await WaitUntil(() => File.Exists(ids)), "the fake scheduler's child never started");
        var child = int.Parse(File.ReadAllText(ids).Trim());

        try
        {
            // A short grace: under the test runner the tray's Ctrl+C is declined (the runner owns a
            // console), so this is also the path where the scheduler is ended outright.
            Assert.True(await process.StopAsync(TimeSpan.FromSeconds(3)), "the scheduler did not end");
            Assert.True(process.HasExited);
            Assert.True(Alive(child), "the scheduler's child must survive: the next scheduler adopts it");
        }
        finally
        {
            Kill(child);
            process.Kill();
        }
    }

    // 670-B, the developer's answer to Q1: Restart used to take the tray down with the scheduler, so
    // nothing started the new one. The scheduler now ends by itself when the tray sets the event named on
    // its command line, which is a request only the scheduler hears - not a console Ctrl+C, which the
    // workers on the same console would hear too.
    [Fact]
    public async Task Stop_asks_the_scheduler_to_end_by_itself_through_its_shutdown_event()
    {
        var name = "670B-Test-" + Guid.NewGuid().ToString("N")[..12];
        var waiting = Path.Combine(_dir, "waiting.txt");
        var bye = Path.Combine(_dir, "bye.txt");
        var script = Path.Combine(_dir, "event-scheduler.ps1");
        File.WriteAllText(script,
            $"$e = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, '{name}')\r\n" +
            $"Set-Content -Path '{waiting}' -Value waiting\r\n" +
            "if ($e.WaitOne(60000)) { " + $"Set-Content -Path '{bye}' -Value bye; exit 0 " + "} exit 3\r\n");

        var log = new LogBuffer();
        using var process = new SchedulerProcess(
            "powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script], log, shutdownEvent: name);
        process.Start();
        Assert.True(await WaitUntil(() => File.Exists(waiting)), "the fake scheduler never started");

        Assert.True(await process.StopAsync(TimeSpan.FromSeconds(30)), "the scheduler did not end");
        Assert.True(process.HasExited);
        Assert.True(File.Exists(bye), "the scheduler ended by itself only when its event was set");
        Assert.Equal(0, process.ExitCode);
        Assert.Contains(log.Snapshot().Lines, l => l.Contains("end by itself", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_a_process_that_has_already_ended_is_not_an_error()
    {
        var script = Path.Combine(_dir, "quick.ps1");
        File.WriteAllText(script, "exit 0\r\n");
        var log = new LogBuffer();
        using var process = new SchedulerProcess("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script], log);
        process.Start();

        Assert.True(await WaitUntil(() => process.HasExited, TimeSpan.FromSeconds(60)), "the fake scheduler never ended");
        Assert.True(await process.StopAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void There_is_no_console_to_signal_for_a_process_that_is_not_there()
    {
        Assert.False(ConsoleSignal.TrySendCtrlC(0));
        Assert.False(ConsoleSignal.TrySendCtrlC(-1));
        Assert.False(ConsoleSignal.TrySendCtrlC(int.MaxValue));
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan? wait = null)
    {
        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(90));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return condition();
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A temp folder the test could not take away is not worth failing over.
        }
    }
}
