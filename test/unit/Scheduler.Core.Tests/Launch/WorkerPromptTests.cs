// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// The prompt a worker session is started with (WRK-10, HLTH-7; <c>Worker-Prompt</c> L1746): every
/// part the worker needs, as one line with no double quotes in it. Test plan: test-plans/workers.md.
/// </summary>
public class WorkerPromptTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";
    private const string LogDir = @"C:\Users\dev\AppData\Local\Proj\scheduler";
    private const string Jobs = "jobs";
    private const string Worktree = @"C:\Projects\Proj-workspaces\slot4";
    private const string Extra = "X";
    private const string CardName = "001-D-test-plan";

    private const string Brief =
        "Category D (design) owns: scheduler-next/spec.md, scheduler-next/test-plans/. "
        + "All categories are defined in jobs/lanes.json.";

    private const string CfgJson = """
        {"promptExtra":"X","lanes":{"D":"design"},"limits":{"maxMinutes":120,"warnMinutes":20}}
        """;

    private static readonly DateTimeOffset Start = new(2026, 11, 18, 11, 20, 0, TimeSpan.Zero);

    private static SchedulerConfig Load(string json) =>
        SchedulerConfigLoader.Load(json, Repo, LocalAppData) with { LogDir = LogDir };

    private static Card Card(string folder) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folder}/{CardName}.md", "Status: ready")
        ?? throw new InvalidOperationException("not a card path");

    private static string Prompt(SchedulerConfig config, Card card, bool resume = false, string categoryName = "") =>
        WorkerPrompt.Build(config, new WorkerPromptRequest
        {
            Card = card,
            Worktree = Worktree,
            Slot = 4,
            Jobs = 8,
            Start = Start,
            Resume = resume,
            CategoryBrief = Brief,
            CategoryName = categoryName,
        });

    // WRK-10 (L1746-1772) and HLTH-7: one assertion per part the rule lists.
    [Fact]
    public void Workers_Prompt_ContainsEveryPart()
    {
        var cfg = Load(CfgJson);
        var prompt = Prompt(cfg, Card("active"), resume: true);

        Assert.Contains(Worktree, prompt);
        Assert.Contains("category D's file rules (design)", prompt);
        Assert.Contains($"{Jobs}/README.md and follow it exactly. It is your whole brief.", prompt);
        Assert.Contains(Brief, prompt);
        Assert.Contains($"Resume card {CardName}, which is already in {Jobs}/active/: an earlier session claimed it", prompt);
        Assert.Contains("Do this one card only, then stop", prompt);
        Assert.Contains("never ask the user anything", prompt);
        Assert.Contains($"git pull --rebase {cfg.Remote} {cfg.Branch}", prompt);
        Assert.Contains($"git push {cfg.Remote} HEAD:{cfg.Branch}", prompt);
        Assert.Contains("wip/001-D", prompt);
        Assert.Contains("Code that has not passed the gates never goes to main", prompt);
        Assert.Contains("use at most 8 threads for builds and tests", prompt);
        Assert.Contains(cfg.JobsEnvVar, prompt);
        Assert.Contains($"says the same. {Extra} Narrate as you go", prompt);
        Assert.Contains("Narrate as you go: before each command or group of reads", prompt);
        Assert.Contains("Today is 2026-11-18, Wednesday by this PC's clock", prompt);
        Assert.Contains("this session gets 120 minutes, until 13:20 by the clock of this PC", prompt);
        Assert.Contains("20 minutes before that, the file", prompt);
        Assert.Contains(WorkerFiles.WarnFlag(LogDir, 4), prompt);
        Assert.Contains(WorkerFiles.MessageFile(LogDir, 4), prompt);
        Assert.Contains(WorkerFiles.StopFlag(LogDir, 4), prompt);
        Assert.Contains("check for three files in one command", prompt);
        Assert.Contains("start your reply with 'Developer:'", prompt);
        Assert.Contains(WorkerFiles.MessageReadFile(LogDir, 4), prompt);
        Assert.Contains("Move-Item -Force", prompt);
        Assert.Contains("Never print a whole file", prompt);
        Assert.Contains("within your first 30 minutes, and then at least every 45 minutes", prompt);
        Assert.Contains("No web: do not fetch web pages", prompt);
        Assert.Contains("no single command may run longer than 10 minutes", prompt);
        Assert.Contains("none may sleep or wait longer than 2 minutes", prompt);
        Assert.Contains("the developer wants you to stop cleanly", prompt);
        Assert.Contains("- Q1:", prompt);
        Assert.Contains(
            "exactly one of: LANE-RESULT: done, LANE-RESULT: review, LANE-RESULT: waiting, "
            + "LANE-RESULT: needs-human, LANE-RESULT: stopped, LANE-RESULT: failed.",
            prompt);

        // It goes on a cmd command line: no double quotes, and one line.
        Assert.DoesNotContain("\"", prompt);
        Assert.DoesNotContain("\n", prompt);
        Assert.DoesNotContain("\r", prompt);

        // CM-9, CM-12 (card 790-A): under the scheduler the card is already claimed when the session
        // starts, and every move the worker wants is a request, not an action of its own.
        var queued = Prompt(Load(CfgJson), Card("queue"));
        Assert.Contains($"Do card {CardName} from {Jobs}/queue/. The scheduler assigned it to you and "
            + $"claimed it: it moved the card into {Jobs}/active/ before starting you, so you have no "
            + "claim step to run", queued);
        Assert.Contains("ask the scheduler to finish the card (to done/ or review/), park it "
            + "(needs-human) or send it back (waiting) with a move request", queued);
        Assert.Contains("Card moves: the scheduler is the only writer of card moves", queued);
        Assert.DoesNotContain("step 3", queued);
        Assert.DoesNotContain("claim it", queued);
        Assert.DoesNotContain("Resume card", queued);

        // The name the scheduler knows the category by now wins over the config's (CFG-13, LANE-5).
        Assert.Contains("category D's file rules (design and notes)", Prompt(Load(CfgJson), Card("queue"), categoryName: "design and notes"));
    }

    // WRK-10, WRK-11: with worker.web true there is no no-web paragraph and no web exclusions.
    [Fact]
    public void Workers_Prompt_WebOn_NoNoWebText()
    {
        var cfg = Load("""
            {"worker":{"web":true},"promptExtra":"X","lanes":{"D":"design"},"limits":{"maxMinutes":120,"warnMinutes":20}}
            """) with { LogDir = LogDir };

        var prompt = Prompt(cfg, Card("active"));
        Assert.DoesNotContain("No web", prompt);
        Assert.DoesNotContain("do not fetch web pages", prompt);

        var args = WorkerArgs.For(cfg, Agents.AgentOf(cfg, null), jobs: 8, model: "m1", endpoint: "", pointer: "p", web: cfg.WorkerWeb);
        Assert.DoesNotContain(args, a => a.Contains("excluded-tools", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.Contains("disallowedTools", StringComparison.Ordinal));
    }
}
