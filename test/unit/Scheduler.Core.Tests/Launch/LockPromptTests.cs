// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// Dynamic locks in the worker's prompt (design <c>dynamic-locks.md</c>, section 3 with 1018-D's spark
/// amendment; test plan section 7, "Prompt"): a scope-mode prompt with the lock exe tells the worker how to
/// ask and how to yield; without it, or in category mode, the prompt is as before.
/// </summary>
public sealed class LockPromptTests
{
    private const string Exe = @"C:\Program Files\ProjectToolkit\scheduler-next.exe";

    private static readonly SchedulerConfig Config = new() { Project = "P", LogDir = @"C:\log", Branch = "main" };

    private static readonly Card TheCard = new()
    {
        Folder = CardFolder.Queue,
        Path = "jobs/queue/1019-A-x.md",
        Name = "1019-A-x",
        Num = 1019,
        Category = "A",
        Id = "1019-A",
        Status = "ready",
    };

    private static string Prompt(string scopeBrief, string exe) => WorkerPrompt.Build(Config, new WorkerPromptRequest
    {
        Card = TheCard,
        Worktree = @"C:\w\slot1",
        Slot = 1,
        Start = new DateTimeOffset(2026, 11, 21, 2, 0, 0, TimeSpan.Zero),
        ScopeBrief = scopeBrief,
        CategoryBrief = "Category brief.",
        LockExe = exe,
    });

    // Prompt: the lock command with the exe's full path, and the yield steps.
    [Fact]
    public void Prompt_ScopeModeWithLocks_HasTheLockCommandAndTheYieldSteps()
    {
        var p = Prompt("You may change: src/.", Exe);

        Assert.Contains($"& '{Exe}' lock <path> --why '<reason>'", p, StringComparison.Ordinal);
        Assert.Contains("wip/1019-A", p, StringComparison.Ordinal);
        Assert.Contains("move request To: active and Status: ready", p, StringComparison.Ordinal);
        Assert.Contains("end with LANE-RESULT: yielded", p, StringComparison.Ordinal);
        Assert.Contains("LANE-RESULT: stopped, LANE-RESULT: yielded, LANE-RESULT: failed.", p, StringComparison.Ordinal);
        Assert.DoesNotContain("stay inside the card's scope", p, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", p, StringComparison.Ordinal);   // still one line with no double quotes
    }

    // Spark (DL-27, section 3): one sentence per exit code, the same text for every agent.
    [Fact]
    public void Prompt_LockText_NamesEveryExitCode()
    {
        var text = WorkerPrompt.LockText(Exe, "1019-A");

        foreach (var answer in new[] { "Exit 0, granted", "Exit 2, waiting", "Exit 3, yield", "Exit 4, refused" })
        {
            Assert.Contains(answer, text, StringComparison.Ordinal);
        }
    }

    // With locks off, the prompt is unchanged.
    [Fact]
    public void Prompt_LocksOff_IsAsBefore()
    {
        var p = Prompt("You may change: src/.", "");

        Assert.Contains("stay inside the card's scope", p, StringComparison.Ordinal);
        Assert.DoesNotContain(" lock <path>", p, StringComparison.Ordinal);
        Assert.DoesNotContain("yielded", p, StringComparison.Ordinal);
    }

    // Category mode (no scope brief) is not lock mode: the exe changes nothing.
    [Fact]
    public void Prompt_CategoryMode_IgnoresTheLockExe()
    {
        Assert.Equal(Prompt("", ""), Prompt("", Exe));
    }
}
