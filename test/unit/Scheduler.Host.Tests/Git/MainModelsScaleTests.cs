// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.State;
using Xunit.Abstractions;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1062-B: state-cache.md section 5's scale rows on a generated repository of 5,000 cards (100 open, 4,900 done),
/// one project. Measured, not unit-tested (section 7): it runs only with <c>PTK_MEASURE_MODEL=1</c>, so the suite
/// does not pay for it, and then holds each row to its budget and prints the numbers.
/// </summary>
public class MainModelsScaleTests(ITestOutputHelper output)
{
    private const int Open = 100;
    private const int Done = 4_900;

    private static string Text(int n, string status)
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"# {n}-B: Generated card {n}\n\nCategory: B\nType: feature\nStatus: {status}\n");
        b.Append(CultureInfo.InvariantCulture, $"Plan: docs/plans/plan-{n % 40}.md, step {n % 9}\nDepends on: {Math.Max(1, n - 1)}-B\nTime box: 60 min\nWorker: Opus\n\n## Task\n\n");
        while (b.Length < 7_800)
        {
            b.Append(CultureInfo.InvariantCulture, $"Line {b.Length} of card {n}: the task, its constraints and what done means.\n");
        }

        b.Append("\n## Result\n\n- done.\n");
        return b.ToString();
    }

    private static string Folder(int n) => n > Open ? "done" : n <= 60 ? "queue" : n <= 90 ? "active" : "review";

    // One fast-import onto refs/remotes/origin/main: the commit header, then the file commands.
    private static void Import(string repo, string? from, string message, Action<StringBuilder> files)
    {
        var s = new StringBuilder();
        s.Append("commit refs/remotes/origin/main\ncommitter t <t@t> 1767225600 +0000\n");
        s.Append(CultureInfo.InvariantCulture, $"data {Encoding.UTF8.GetByteCount(message)}\n{message}\n");
        if (from is not null)
        {
            s.Append(CultureInfo.InvariantCulture, $"from {from}\n");
        }

        files(s);
        s.Append('\n');
        var psi = new ProcessStartInfo(GitExecutable.Path) { WorkingDirectory = repo, RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
        _ = GitExecutable.Apply(psi);
        psi.ArgumentList.Add("fast-import");
        psi.ArgumentList.Add("--quiet");
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var bytes = Encoding.UTF8.GetBytes(s.ToString());
        p.StandardInput.BaseStream.Write(bytes);
        p.StandardInput.Close();
        Assert.True(p.WaitForExit(120_000), "fast-import did not end");
        Assert.True(p.ExitCode == 0, err.Result);
    }

    private static void Inline(StringBuilder s, string path, string text) =>
        s.Append(CultureInfo.InvariantCulture, $"M 100644 inline {path}\ndata {Encoding.UTF8.GetByteCount(text)}\n{text}\n");

    private double TimedTick(MainModelsRepository repo)
    {
        var t = Stopwatch.GetTimestamp();
        Assert.True(repo.Tick());
        return Stopwatch.GetElapsedTime(t).TotalMilliseconds;
    }

    [Fact]
    public void Scale_5000Cards_StartupAndRefreshRows_WithinBudget()
    {
        if (Environment.GetEnvironmentVariable("PTK_MEASURE_MODEL") != "1")
        {
            return;   // measured on request only
        }

        using var fx = new GitFixture();
        var build = Stopwatch.GetTimestamp();
        Import(fx.Repo, null, "start", s =>
        {
            for (var n = 1; n <= Open + Done; n++)
            {
                var folder = Folder(n);
                Inline(s, $"jobs/{folder}/{n}-B-card-{n}.md", Text(n, folder == "done" ? "done" : "ready"));
            }

            Inline(s, "jobs/scheduler.json", "{ \"pollSeconds\": 15 }\n");
            Inline(s, "jobs/README.md", "# The job queue\n");
        });
        var buildMs = Stopwatch.GetElapsedTime(build).TotalMilliseconds;
        string Head() => GitFixture.Git(fx.Repo, "rev-parse", "refs/remotes/origin/main");

        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var log = new ListLog();
        var models = new MainModels(5);
        var started = Stopwatch.GetTimestamp();
        var repo = models.Join(new MainModelsJoin(fx.Repo, "origin", "main", "jobs", TimeSpan.FromSeconds(15), false,
            new GitRunner(), log, new FakeClock(DateTimeOffset.Now)));
        Assert.True(Ended(repo.Loading));
        var wall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var model = repo.Model!;
        Assert.Equal(Open, model.Cards("jobs").Count);
        Assert.Equal(Done, model.DoneCards("jobs").Count);
        GC.Collect();
        var bytes = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(model);

        // One card moved queue -> active.
        Import(fx.Repo, Head(), "jobs: start 1-B", s => s.Append("R jobs/queue/1-B-card-1.md jobs/active/1-B-card-1.md\n"));
        var move = TimedTick(repo);

        // One card moved into done/ of 4,900.
        Import(fx.Repo, Head(), "jobs: done 61-B", s => s.Append("R jobs/active/61-B-card-61.md jobs/done/61-B-card-61.md\n"));
        var intoDone = TimedTick(repo);

        // A planner batch of 20 new cards.
        Import(fx.Repo, Head(), "planner: 20 cards", s =>
        {
            for (var n = 6_001; n <= 6_020; n++)
            {
                Inline(s, $"jobs/queue/{n}-B-card-{n}.md", Text(n, "ready"));
            }
        });
        var batch = TimedTick(repo);
        Assert.Equal(Open + 20 - 1, repo.Model!.Cards("jobs").Count);
        Assert.Equal(Done + 1, repo.Model.DoneCards("jobs").Count);

        // An idle tick: nothing moved.
        const int Idle = 200;
        var idle = Stopwatch.GetTimestamp();
        for (var i = 0; i < Idle; i++)
        {
            Assert.False(repo.Tick());
        }

        var idleMs = Stopwatch.GetElapsedTime(idle).TotalMilliseconds / Idle;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"scale: built {Open + Done} cards in {buildMs:F0} ms; phase 1 {repo.PhaseOneMs} ms, phase 2 {repo.PhaseTwoMs} ms, both {wall:F0} ms; "
            + $"model {bytes / 1024.0 / 1024.0:F1} MB; queue->active {move:F1} ms; into done {intoDone:F1} ms; 20 new {batch:F1} ms; idle {idleMs:F3} ms"));
        foreach (var line in log.Lines)
        {
            output.WriteLine(line);
        }

        // Section 5's budgets.
        Assert.True(repo.PhaseOneMs <= 1_000, $"phase 1 {repo.PhaseOneMs} ms");
        Assert.True(repo.PhaseTwoMs <= 4_000, $"phase 2 {repo.PhaseTwoMs} ms");
        Assert.True(bytes <= 8L * 1024 * 1024, $"model {bytes} bytes");
        Assert.True(move <= 20, $"queue->active {move} ms");
        Assert.True(intoDone <= 50, $"into done {intoDone} ms");
        Assert.True(batch <= 100, $"20 new {batch} ms");
        Assert.True(idleMs <= 5, $"idle {idleMs} ms");
    }

    private static bool Ended(Task load) => load.Wait(TimeSpan.FromSeconds(120));
}
