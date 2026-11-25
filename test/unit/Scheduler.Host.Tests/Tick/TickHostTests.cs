// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// The Host side of the tick's worker steps (285-B), over a temporary repository with a bare
/// remote and a fake agent CLI.
/// </summary>
[ParallelMethods]
public class TickHostTests
{
    private const string Ends =
        "echo ARGS: %*\r\necho CWD: %CD%\r\n"
        + "mkdir jobs\\done\r\n"
        + "git mv jobs/active/010-B-x.md jobs/done/010-B-x.md\r\n" // 660-B: the scheduler claimed it before the worker started
        + "git -c user.name=w -c user.email=w@w commit -qm \"jobs: done 010-B-x\"\r\n"
        + "git push -q origin HEAD:main\r\n"
        + "echo LANE-RESULT: done\r\n";

    // 660-B: a worker that hands its finished card to the scheduler with a move request instead of git mv.
    private const string HandsOver =
        "echo ARGS: %*\r\n"
        + "(echo Card: 010-B-x& echo To: done& echo Slot: 1& echo ---& type jobs\\active\\010-B-x.md& echo - all done by request) > \"{LOGDIR}\\moves\\20991231-235959-999-slot1.move.tmp\"\r\n"
        + "move /y \"{LOGDIR}\\moves\\20991231-235959-999-slot1.move.tmp\" \"{LOGDIR}\\moves\\20991231-235959-999-slot1.move\" >nul\r\n"
        + "echo LANE-RESULT: done\r\n";
    private const string NeverEnds = "echo started\r\nping -n 600 127.0.0.1 >nul\r\n";

    // 959-A (WT-6, WT-C4, WT-W1): the type WT-5 chose is who runs, whatever folder the worker lands in.
    // Three cards, one worker at a time, all in folder 1: a resumed orphan and a queued card that ask
    // for the premium Claude type start the claude CLI with that type's model, and the card with no
    // Worker: line starts Copilot on the default agent.
    [Fact]
    public void Start_TypesRunInAnyFolder_EachUsesItsOwnTypeAgentAndModel()
    {
        const string Workers = """
            {"pool":{"maxWorkers":1,"types":{
               "default":{"max":1,"agent":"","model":"","premium":false,"areas":"A"},
               "Opus":{"max":1,"agent":"claude","model":"claude-opus-5-5","premium":true,"areas":""}}}}
            """;
        // The fake claude CLI hands over the cards it is given and records the arguments it ran with.
        const string Claude =
            "echo ARGS: %*>>\"{LOGDIR}\\claude-args.txt\"\r\n"
            + "if exist jobs\\active\\012-A-orphan.md (\r\n"
            + "mkdir jobs\\done 2>nul\r\n"
            + "git mv jobs/active/012-A-orphan.md jobs/done/012-A-orphan.md\r\n"
            + "git -c user.name=w -c user.email=w@w commit -qm \"jobs: done 012-A-orphan\"\r\n"
            + "git push -q origin HEAD:main\r\n"
            + "echo LANE-RESULT: done\r\n"
            + ") else (\r\n"
            + "mkdir jobs\\done 2>nul\r\n"
            + "git mv jobs/active/011-A-prem.md jobs/done/011-A-prem.md\r\n"
            + "git -c user.name=w -c user.email=w@w commit -qm \"jobs: done 011-A-prem\"\r\n"
            + "git push -q origin HEAD:main\r\n"
            + "echo LANE-RESULT: done\r\n"
            + ")\r\n";
        using var rig = new TickRig(
            EndsFor("010-A-free"),
            workersJson: Workers,
            claudeCliScript: Claude,
            cards: new Dictionary<string, string>
            {
                ["jobs/queue/010-A-free.md"] = "# 010-A: free\n\nLane: A\nStatus: ready\n",
                ["jobs/queue/011-A-prem.md"] = "# 011-A: premium\n\nLane: A\nStatus: ready\nWorker: Opus\n",
                ["jobs/active/012-A-orphan.md"] = "# 012-A: resumed\n\nLane: A\nStatus: ready\nClaimed: 2026-11-19, lane A\nWorker: Opus\n",
            });

        // Each card in turn, as the scheduler came to know it: card id -> the worker it was started as.
        var seen = new Dictionary<string, RunningWorker>(StringComparer.OrdinalIgnoreCase);
        bool StartedAs(string id, string marker)
        {
            foreach (var w in rig.Composition.Tick.Running.Values)
            {
                seen.TryAdd(w.Id, w);
            }

            return seen.TryGetValue(id, out var w2) && w2.Slot == 1
                && rig.Marker(marker).Contains(id + "-", StringComparison.Ordinal);
        }

        // The orphan first (DISP-14): the claude CLI, the Opus type's model, folder 1.
        Assert.True(rig.TickUntilSettled(() => StartedAs("012-A", "claude-args.txt")), string.Join("\n", rig.Log.Lines));
        var resumed = seen["012-A"];
        Assert.Equal("Opus", resumed.Type);
        Assert.Equal("claude", resumed.Agent);
        Assert.Equal("claude-opus-5-5", resumed.Model);
        Assert.Contains("claude-opus-5-5", rig.Marker("claude-args.txt"));

        // Then the card with no Worker: line: the default agent's CLI, never the claude one.
        Assert.True(rig.TickUntilSettled(() => StartedAs("010-A", "default-args.txt")), string.Join("\n", rig.Log.Lines));
        var free = seen["010-A"];
        Assert.Equal("default", free.Type);
        Assert.DoesNotContain("claude", free.Agent, StringComparison.Ordinal);
        Assert.DoesNotContain("claude-opus-5-5", rig.Marker("default-args.txt"));

        // Then the queued premium card, in the very folder the default type last used.
        Assert.True(rig.TickUntilSettled(() => StartedAs("011-A", "claude-args.txt")), string.Join("\n", rig.Log.Lines));
        var premium = seen["011-A"];
        Assert.Equal(1, premium.Slot);
        Assert.Equal("Opus", premium.Type);
        Assert.Equal("claude", premium.Agent);
        Assert.Equal("claude-opus-5-5", premium.Model);
    }

    // The fake CLI ends its run by handing its own card to the scheduler, as Ends does for 010-B-x.
    private static string EndsFor(string card) =>
        "echo ARGS: %*>\"{LOGDIR}\\default-args.txt\"\r\n"
        + "mkdir jobs\\done 2>nul\r\n"
        + $"git mv jobs/active/{card}.md jobs/done/{card}.md\r\n"
        + $"git -c user.name=w -c user.email=w@w commit -qm \"jobs: done {card}\"\r\n"
        + "git push -q origin HEAD:main\r\n"
        + "echo LANE-RESULT: done\r\n";

    // WRK-10, TIME-21: a ready card is started in its slot's worktree and the fake CLI is given the prompt pointer.
    [Fact]
    public void Tick_StartWorker_RunsTheCardInItsSlot()
    {
        using var rig = new TickRig(Ends);
        Assert.True(rig.TickUntilSettled(() => rig.LogFiles("010-B-2*.log").Length > 0));

        var log = ExactlyOne(rig, "worker logs", rig.LogFiles("010-B-2*.log"));
        var prompt = ExactlyOne(rig, "prompts", rig.LogFiles("*.prompt.txt"));
        Assert.True(rig.TickUntilSettled(() => TickRig.ReadShared(log).Contains("LANE-RESULT", StringComparison.Ordinal)));
        var text = TickRig.ReadShared(log);
        Assert.Contains("Your whole brief is in the file", text);
        Assert.Contains(prompt, text);
        Assert.Contains(Path.Combine(rig.Config.WorkspaceRoot, "slot1"), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do card 010-B-x", File.ReadAllText(prompt));
    }

    // TIME-1 to TIME-12: past the limit the worker is warned, asked to stop, then killed after the grace time.
    [Fact]
    public void Tick_Watch_OverLimit_StopRequestedThenKilled()
    {
        using var rig = new TickRig(NeverEnds);
        Assert.True(rig.TickUntilSettled(() => rig.Composition.Tick.Running.Count == 1)); // 1026-B: on events, not a 60 s clock
        var worker = rig.Composition.Tick.Running[1];
        var warn = Path.Combine(rig.LogDir, "warn-slot1");
        var stop = Path.Combine(rig.LogDir, "stop-slot1");
        Assert.False(File.Exists(warn));

        var start = worker.Start; // 1051-B: each step from the start, not from now, so slow setup cannot skip one
        rig.Clock.MoveTo(start + TimeSpan.FromMinutes(8.5)); // limit 10, warning lead 2
        rig.Composition.Tick.Run();
        Assert.True(File.Exists(warn));
        Assert.False(File.Exists(stop));
        Assert.Contains("minutes are left", File.ReadAllText(warn));

        rig.Clock.MoveTo(start + TimeSpan.FromMinutes(10.5));
        rig.Composition.Tick.Run();
        Assert.True(File.Exists(stop));
        Assert.NotNull(rig.Composition.Tick.Running[1].StopAt);

        rig.Clock.MoveTo(start + TimeSpan.FromMinutes(12.6)); // grace 2 min after the request (pinned at 10.5)
        rig.Composition.Tick.Run();
        Assert.NotNull(rig.Composition.Tick.Running[1].KillTime);
        // The next tick reaps the killed worker (its accounting row). Its card stays in active/ and is resumed at once by
        // a second worker that never ends either, so the check is the first worker's row and that it no longer runs:
        // waiting for an empty Running waited the rig's whole 2 minute fallback on the second worker.
        var csv = Path.Combine(rig.LogDir, "accounting.csv");
        Assert.True(rig.TickUntilSettled(() => File.Exists(csv) && File.ReadAllText(csv).Contains("010-B-x", StringComparison.Ordinal)));
        Assert.DoesNotContain(rig.Composition.Tick.Running.Values, w => w.Pid == worker.Pid);
        Assert.Contains(rig.Log.Lines, l => l.Contains("killed", StringComparison.Ordinal));
        Assert.Contains("010-B-x", File.ReadAllText(csv));
    }

    // 660-B: the scheduler claims the card before the worker starts, and applies the worker's move
    // request when it ends, before the result is settled: one copy, in done/, with the worker's text.
    [Fact]
    public void Tick_ClaimAtStart_FinishByRequest_OneCopy()
    {
        using var rig = new TickRig(HandsOver);
        Assert.True(rig.TickUntilSettled(() => rig.RemoteHas("jobs/done/010-B-x.md") && rig.Composition.Tick.Running.Count == 0));
        Assert.False(rig.RemoteHas("jobs/queue/010-B-x.md"));
        Assert.False(rig.RemoteHas("jobs/active/010-B-x.md"));
        var text = GitFixture.Git(rig.Fx.Bare, "show", "main:jobs/done/010-B-x.md");
        Assert.Contains("Status: done", text);
        Assert.Contains("Claimed: ", text);
        Assert.Contains("- all done by request", text);
        Assert.Contains(rig.Log.Lines, l => l.Contains("card move from the scheduler's claim: 010-B-x queue/ -> active/", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.Contains("card move from slot 1: 010-B-x active/ -> done/", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.Contains("finished: 010-B-x -> done", StringComparison.Ordinal));
    }

    // TIME-13 to TIME-20: LANE-RESULT: done from the fake CLI ends with an accounting row and the slot idle.
    [Fact]
    public void Tick_Finish_ResultLineSettlesTheCard()
    {
        using var rig = new TickRig(Ends);
        // 1005-B: no wall-clock limit - each tick waits for the git writer and the worker's exit.
        Assert.True(rig.TickUntilSettled(() => rig.LogFiles("accounting.csv").Length > 0 && File.ReadAllText(Path.Combine(rig.LogDir, "accounting.csv")).Contains("010-B-x", StringComparison.Ordinal)), string.Join("\n", rig.Log.Lines));
        if (rig.Composition.Tick.Running.Count != 0)
        {
            Assert.Fail("Running still holds " + rig.Composition.Tick.Running.Count + " card(s) after the card settled.\n" + string.Join("\n", rig.Log.Lines));
        }

        var csv = File.ReadAllText(Path.Combine(rig.LogDir, "accounting.csv"));
        Assert.Contains("010-B-x", csv);
        Assert.Contains("done", csv);
        Assert.Contains(rig.Log.Lines, l => l.Contains("finished: 010-B-x -> done", StringComparison.Ordinal));
        Assert.Empty(rig.LogFiles("stop-slot*"));
    }

    // 460-B, TIME-14: a Claude worker's LANE-RESULT in its own text is read from the rendered log: done, not "no log".
    [Fact]
    public void Finish_ClaudeRun_ResultReadFromRenderedLog() =>
        FinishesDone(AgentFormat.Claude, "echo {\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"LANE-RESULT: done\"}]}}\r\n");

    // 460-B, TIME-14: the same for a Copilot worker's events.
    [Fact]
    public void Finish_CopilotRun_ResultReadFromRenderedLog() =>
        FinishesDone(AgentFormat.Copilot, "echo {\"type\":\"assistant.message\",\"data\":{\"content\":\"LANE-RESULT: done\"}}\r\n");

    // WRK-14, 700-B: a Claude worker is handed its messages by the hook, so its prompt has no
    // "check the message file" step; a Copilot worker still reads the file itself.
    [Fact]
    public void Start_Worker_PromptMessageCheckFollowsTheAgentFormat()
    {
        var (hooked, hookMsg) = PromptOf(AgentFormat.Claude);
        Assert.DoesNotContain(hookMsg, hooked, StringComparison.Ordinal);
        Assert.DoesNotContain("msg-slot1", hooked, StringComparison.Ordinal);
        Assert.Contains("two files in one command", hooked, StringComparison.Ordinal);
        Assert.Contains("The scheduler hands each message to you", hooked, StringComparison.Ordinal);

        var (plain, ownMsg) = PromptOf(AgentFormat.Copilot);
        Assert.Contains(ownMsg, plain, StringComparison.Ordinal);
        Assert.Contains("msg-slot1.read.txt", plain, StringComparison.Ordinal);
        Assert.Contains("three files in one command", plain, StringComparison.Ordinal);
    }

    // PLAN-2: a complete batch in the outbox is committed and pushed to the remote.
    [Fact]
    public void Tick_PlannerStep_CommitsAnOutboxBatch()
    {
        using var rig = new TickRig(Ends, "outbox");
        var batch = Path.Combine(rig.Fx.Repo, "outbox", "b1");
        Directory.CreateDirectory(batch);
        File.WriteAllText(Path.Combine(batch, "020-B-new.md"), "# 020-B: new\n\nLane: B\nStatus: ready\n");
        File.WriteAllText(Path.Combine(batch, "batch.json"), JsonSerializer.Serialize(new
        {
            message = "planner: add 020-B",
            trailers = new[] { "Co-Authored-By: P <p@x>" },
            ops = new object[] { new { op = "write", path = "jobs/queue/020-B-new.md", @base = "new", file = "020-B-new.md" } },
        }));
        File.WriteAllText(Path.Combine(rig.LogDir, "planner-state.json"), "{\"push\":true}");

        rig.Composition.Host.PlannerStep();

        // 987-B: the batch is pushed by the git writer, after the step has returned.
        Assert.True(rig.Composition.Host.Writer!.WaitIdle(TimeSpan.FromMinutes(10))); // 1051-B: waits on the writer; the cap only turns a hang into a failure
        Assert.True(rig.RemoteHas("jobs/queue/020-B-new.md"));
    }

    // 987-B: a planner step with a slow push waiting returns at once; the push lands on the writer later.
    [Fact]
    public void Tick_PlannerStep_ReturnsBeforeThePushFinishes()
    {
        using var rig = new TickRig(Ends);
        var host = rig.Composition.Host;
        using var push = new ManualResetEventSlim(false);
        var pushed = 0;
        host.Writes = () =>
        {
            push.Wait(TimeSpan.FromSeconds(30));
            Interlocked.Increment(ref pushed);
            return false;
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        host.PlannerStep();
        host.PlannerStep();   // at most one more waits behind the running one
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the step took {watch.Elapsed}");
        Assert.Equal(0, pushed);
        push.Set();
        Assert.True(host.Writer!.WaitIdle(TimeSpan.FromSeconds(30)));
        Assert.InRange(pushed, 1, 2);
    }

    // 987-B: the planner's release of a card's retries runs on the tick's thread, at its next step.
    [Fact]
    public void Tick_OnTick_RunsAtTheNextPlannerStep()
    {
        using var rig = new TickRig(Ends);
        var host = rig.Composition.Host;
        host.Writes = () => false;
        var ran = 0;
        host.OnTick(() => ran++);

        Assert.Equal(0, ran);
        host.PlannerStep();
        Assert.Equal(1, ran);
    }

    // CFG-15: the composed program takes a card from queue/ to done/ through the fake CLI and exits on Drain.
    [Fact]
    public async Task Program_EndToEnd_QueueToDone()
    {
        using var rig = new TickRig(Ends);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30)); // 1051-B: a hang cap only
        var run = Task.Run(() => rig.Composition.Runner.Run(cts.Token));

        var until = DateTime.UtcNow.AddMinutes(15); // 1026-B: fallbacks only; 1051-B: a loaded PC took 216 s for one tick
        while (DateTime.UtcNow < until && !rig.RemoteHas("jobs/done/010-B-x.md"))
        {
            Thread.Sleep(300);
        }

        if (!rig.RemoteHas("jobs/done/010-B-x.md"))
        {
            Assert.Fail(string.Join("\n", rig.Log.Lines.TakeLast(4))
                + "\nFILES: " + string.Join(" | ", Directory.GetFiles(rig.LogDir).Select(Path.GetFileName))
                + "\nLOG: " + string.Join("\n", rig.LogFiles("010-B-2*.log").Take(1).Select(TickRig.ReadShared)));
        }

        Assert.False(rig.RemoteHas("jobs/queue/010-B-x.md"));

        // Wait for the exit to be settled, then drain.
        until = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < until && rig.Composition.Tick.Running.Count > 0)
        {
            Thread.Sleep(300);
        }

        rig.Composition.Tick.State = SchedulerState.Draining;
        rig.Composition.Runner.RequestTick();
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromMinutes(5))));
        await run;
    }

    // DISP-21: a dry run composed from the real ports logs the start and exits after the first tick.
    [Fact]
    public void Program_DryRun_LogsAndExitsAfterFirstTick()
    {
        using var rig = new TickRig(Ends, dryRun: true);
        rig.Composition.Runner.Run();
        Assert.Contains(rig.Log.Lines, l => l.Contains("DRY RUN worker in slot 1 <- 010-B-x", StringComparison.Ordinal));
        Assert.Empty(rig.LogFiles("*.prompt.txt"));
    }

    // The one file of a kind; when there are none or several, fail with the count, the names and the scheduler's whole log.
    private static string ExactlyOne(TickRig rig, string what, string[] found)
    {
        if (found.Length != 1)
        {
            Assert.Fail("Expected exactly 1 of " + what + ", found " + found.Length + ": " + string.Join(", ", found.Select(Path.GetFileName))
                + "\nSCHEDULER LOG:\n" + string.Join("\n", rig.Log.Lines));
        }

        return found[0];
    }

    private static void FinishesDone(AgentFormat format, string resultLine)
    {
        var script = Ends.Replace("echo LANE-RESULT: done\r\n", resultLine, StringComparison.Ordinal);
        using var rig = new TickRig(script, format: format);
        var csv = Path.Combine(rig.LogDir, "accounting.csv");
        Assert.True(rig.TickUntilSettled(() => File.Exists(csv) && File.ReadAllText(csv).Contains("010-B-x", StringComparison.Ordinal)));

        Assert.Contains(rig.Log.Lines, l => l.Contains("finished: 010-B-x -> done", StringComparison.Ordinal));
        _ = ExactlyOne(rig, "raw worker logs", rig.LogFiles("010-B-2*.jsonl"));   // 1307-B: a second one is a card started twice
        var view = ExactlyOne(rig, "worker logs", rig.LogFiles("010-B-2*.log"));
        Assert.Contains("* LANE-RESULT: done", File.ReadAllLines(view));
        Assert.DoesNotContain(File.ReadAllLines(view), l => l.StartsWith('{'));
    }

    // The prompt a worker of this format was started with, and the message file named in it (700-B).
    private static (string Prompt, string MessageFile) PromptOf(AgentFormat format)
    {
        using var rig = new TickRig(Ends, format: format);
        var csv = Path.Combine(rig.LogDir, "accounting.csv");
        Assert.True(rig.TickUntilSettled(() => File.Exists(csv) && File.ReadAllText(csv).Contains("010-B-x", StringComparison.Ordinal)));
        return (File.ReadAllText(ExactlyOne(rig, "prompts", rig.LogFiles("*.prompt.txt"))), WorkerFiles.MessageFile(rig.LogDir, 1));
    }
}
