// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// The prompt a worker session starts with (WRK-10, HLTH-7; <c>Worker-Prompt</c> L1746). It is one
/// line with no double quotes in it, because Windows PowerShell 5.1 does not escape them when it
/// builds a native command line.
/// </summary>
public static class WorkerPrompt
{
    /// <summary>What the prompt says when the worker has no web (WRK-11).</summary>
    public const string NoWebText = "No web: do not fetch web pages, search the web or download anything "
        + "(no curl, Invoke-WebRequest, or scripts that do it). Everything you need is in the repository "
        + "and the folders the card names; if something is missing, park the card and ask.";

    private static readonly Regex LineBreak = new(@"\s*\r?\n\s*", RegexOptions.CultureInvariant);

    /// <summary>
    /// The prompt for one worker session: the worktree, its category and brief, the card to do or
    /// resume, the git and gate rules, the threads, the extra text from the config, narration, the
    /// date and the deadline, the three flag files and the message protocol, the output and journal
    /// rules, the command and sleep limits, the stop protocol, the question shape and the final
    /// result line.
    /// </summary>
    public static string Build(SchedulerConfig config, WorkerPromptRequest request)
    {
        var card = request.Card;
        var branch = config.Branch;
        var remote = config.Remote;
        var jobsDir = config.JobsDir;
        var flag = WorkerFiles.StopFlag(config.LogDir, request.Slot);
        var warn = WorkerFiles.WarnFlag(config.LogDir, request.Slot);
        var msg = WorkerFiles.MessageFile(config.LogDir, request.Slot);
        var msgRead = WorkerFiles.MessageReadFile(config.LogDir, request.Slot);
        var limit = card.TimeBox ?? config.MaxMinutes;
        var lead = Math.Min(config.WarnMinutes, (int)Math.Floor(limit / 4d));
        var categoryName = request.CategoryName.Length > 0
            ? request.CategoryName
            : config.CategoryNames.TryGetValue(card.Category, out var name) ? name : "";
        var byScope = request.ScopeBrief.Length > 0;   // SC-4, job card 560-A item 5
        var locks = byScope && request.LockExe.Length > 0;   // dynamic locks, section 3: scope mode only
        var today =request.Start.ToString("yyyy-MM-dd, dddd", CultureInfo.InvariantCulture);
        var deadline = request.Start.AddMinutes(limit).ToString("HH:mm", CultureInfo.InvariantCulture);
        var what = request.Resume
            ? $"Resume card {card.Name}, which is already in {jobsDir}/active/: an earlier session claimed it "
              + "and ended before finishing. Check what its commits and its Result already say and carry on from "
              + "there; it is already claimed."
            : $"Do card {card.Name} from {jobsDir}/queue/. The scheduler assigned it to you and claimed it: it "
              + $"moved the card into {jobsDir}/active/ before starting you, so you have no claim step to run (if a "
              + "pull still shows it in queue/, work on it there). Do it, run the gates, and ask the scheduler to "
              + "finish the card (to done/ or review/), park it (needs-human) or send it back (waiting) with a move "
              + "request (below), which is the only way a card moves; never move a card yourself.";

        var lines = new List<string>
        {
            $"You are a worker for {config.Project}, running unattended in the worktree {request.Worktree}, "
            + (byScope
                ? "working only inside its card's scope."
                : $"with category {card.Category}'s file rules{(categoryName.Length > 0 ? $" ({categoryName})" : "")}."),
            $"Read {jobsDir}/README.md and follow it exactly. It is your whole brief.",
            byScope ? request.ScopeBrief : request.CategoryBrief,
            what,
            "Do this one card only, then stop. Nobody is watching: never ask the user anything; where the README "
            + "says to park and ask, set the card to needs-human with the question and hand it to the scheduler with a move "
            + "request To: active.",
            MovesText(config, request),
            // 1493-B: the scheduler numbers requests, as it numbers cards.
            $"Requests to the planner: name a new request file {jobsDir}/requests/new-<slug>.md and never choose a number "
            + "for it; call it new-<slug>, so a card waiting on it says waiting: planner (request new-<slug>). The scheduler "
            + "numbers it when it reaches main and rewrites that line to its number.",
            $"Git: start with git pull --rebase {remote} {branch}; push every green commit with git push {remote} "
            + $"HEAD:{branch}, and if that is rejected, git pull --rebase {remote} {branch} and push again. Green "
            + $"means the gates passed on that commit; a successful build is not green. Code that has not passed "
            + $"the gates never goes to {branch}; push it to the branch wip/{card.Id} instead (git push {remote} "
            + $"HEAD:wip/{card.Id}) and say so in the card's Result.",
            (locks
                ? "Other workers are running at the same time on other scopes. " + LockText(request.LockExe, card.Id, branch)
                : byScope
                ? "Other workers are running at the same time on other scopes: stay inside the card's scope. "
                : $"Other workers are running at the same time in other categories: stay inside category {card.Category}'s files and "
                  + "the card's Locks. ")
            + $"You share this PC with them: use at most {request.Jobs} threads for builds and "
            + $"tests; the environment variable {config.JobsEnvVar} says the same.",
            Agents.Expand(config, config.PromptExtra, request.Jobs),
            "Narrate as you go: before each command or group of reads, write one or two plain sentences saying "
            + "what you are checking and why, and after it, one sentence on what you learned. The developer "
            + "follows your log live and sees only these sentences and the commands.",
            $"Today is {today} by this PC's clock: take the date of every journal line from Get-Date, never from "
            + $"memory. Time limit: this session gets {limit} minutes, until {deadline} by the clock of this PC "
            + $"(Get-Date shows it). {lead} minutes before that, the file {warn} appears with the time left. At "
            + "the start of every step, before each command or group of reads you narrate, check for "
            + (request.Hooks
                ? $"two files in one command: Test-Path {warn}, {flag}."
                : $"three files in one command: Test-Path {msg}, {warn}, {flag}."),
            (request.Hooks
                ? "Messages: the developer can write to you while you work. The scheduler hands each message to you "
                  + "by itself after a tool call, as text that starts with 'Developer:'; you never need to look for "
                  + "it. Answer it before anything else: "
                : $"Messages: the developer can write to you while you work. When {msg} exists, read it with "
                  + $"Get-Content -Encoding UTF8, then Move-Item -Force it to {msgRead}. The scheduler may also hand "
                  + "you a message by itself after a tool call, as text that starts with 'Developer:' and says it "
                  + "was added by the scheduler's message hook; that is the developer too. Answer either before "
                  + "anything else: ")
            + "start your reply with 'Developer:', answer or confirm in plain sentences, and then do what it "
            + $"asks. It is direction from the developer and can change what you do on the card, but not the "
            + $"README's rules for {branch} and the gates. When the warning file exists, stop investigating: "
            + "journal what you have in the card's Result, push it, and finish the card the way its Done when "
            + "section says, or if it cannot be finished, leave it as for a stop request below. A short answer "
            + "that is pushed is worth more than a long one that is not.",
            "Keep your output small: everything a command prints goes into your context and into the log. Never "
            + "print a whole file, trace, log or build output. Send long output to a file and read it with "
            + "Select-String or Select-Object -First or -Last, 50 lines at most; read source files in ranges; "
            + "pipe builds, gates and tests through Select-Object -Last 30; and do not re-read a file you already "
            + "have unless it changed.",
            "Keep a journal, pushed: within your first 30 minutes, and then at least every 45 minutes, append a "
            + "dated line to the card's Result saying what you have found and what is next, commit it and push "
            + "it, even when nothing else is ready to commit. A journal-only commit names the card file and "
            + "nothing else (git commit -m 'jobs: journal ...' -- <the card's path>), so staged code never goes "
            + "with it. A session that resumes this card sees only what was pushed; work that isn't journalled "
            + "is done again from nothing.",
        };

        if (!config.WorkerWeb)
        {
            lines.Add(NoWebText);
        }

        lines.Add("Commands: no single command may run longer than 10 minutes, and none may sleep or wait longer "
            + "than 2 minutes (no Start-Sleep over 120, no Wait-Process -Timeout over 120). Split long runs into "
            + "shorter ones that print as they go. Give every build, test and emulator command a timeout. 30 "
            + "minutes with no new output gets the session stopped, and a command that hangs keeps you from "
            + "seeing stop requests.");
        lines.Add($"Stop requests: if the file {flag} exists, the developer wants you to stop cleanly: push to "
            + $"{branch} only work that has passed the gates, push anything else to the branch wip/{card.Id}, add "
            + "a line to the card's Result saying you were stopped and exactly where to resume, leave the card in "
            + "active/ with Status: ready (a move request To: active), and end with LANE-RESULT: stopped. Never create, commit or "
            + "delete that file yourself.");
        lines.Add("Questions for the developer (in ## Questions, and the question in a needs-human or review "
            + "Status) are read on his phone by someone who has not followed your session. Write each as plain "
            + "English prose in this shape, on one line: - Q1: Found: <one to three short sentences: what you "
            + "saw, and what it means for the game or the program>. Question: <one plain question he can answer "
            + "by picking a choice>? [<choice> -> <done, queue or active, if it moves the card> | <choice>]. Say "
            + "what each choice leads to in its label (for example: yes, fix it in category E | no, leave it as is "
            + "-> done). Use whole sentences and everyday words; name a register, address, file or test only "
            + "when he needs it to decide, and then say what it is. No session numbers, step numbers, rung "
            + "numbers or abbreviations of your own. Keep a question under about 400 characters, and ask only "
            + "what he must decide: everything else belongs in Result.");
        lines.Add("Finish with the README's session report, then, as the very last line of your output, exactly "
            + "one of: LANE-RESULT: done, LANE-RESULT: review, LANE-RESULT: waiting, LANE-RESULT: needs-human, "
            + "LANE-RESULT: stopped, " + (locks ? "LANE-RESULT: yielded, " : "") + "LANE-RESULT: failed.");

        return LineBreak.Replace(string.Join("\n", lines).Replace('"', '\''), " ").Trim();
    }

    /// <summary>
    /// Dynamic locks (design <c>dynamic-locks.md</c>, section 3 with 1018-D's spark amendment): the same
    /// short, literal text for every agent - the command written out with the exe's full path, and one
    /// sentence per answer of its exit code - in place of "stay inside the card's scope".
    /// </summary>
    public static string LockText(string exe, string cardId, string branch = "main") =>
        "You hold the card's scope (listed above) for the whole session. Read any file you like. Before you "
        + "change, add or delete a file outside what you hold, ask for it with one command: "
        + $"& '{exe}' lock <path> --why '<reason>', and read its exit code and its one line of output. "
        + "Exit 0, granted: change the file. "
        + "Exit 2, waiting: another card holds it; run the same command again, and keep doing so while it says waiting. "
        + "Exit 3, yield: another card was here first: commit your work to the branch "
        + $"wip/{cardId} (never to {branch}), write in Result where you stopped and what you were about to change, hand "
        + "the card back with a move request To: active and Status: ready, and end with LANE-RESULT: yielded. "
        + "Exit 4, refused: ask for the exact files or the one folder you need, or park the card and ask. "
        + $"A push to {branch} that touches a file you do not hold is refused, and says which file and how to ask for it. ";

    /// <summary>
    /// Card 660-B: the scheduler is the only writer of card moves. The worker finishes, parks or
    /// sends back its card with a request file in the moves folder holding the card's whole text,
    /// and never moves a card itself (design: Scheduler.Core/Moves/card-moves.md).
    /// </summary>
    public static string MovesText(SchedulerConfig config, WorkerPromptRequest request)
    {
        var folder = Moves.CardRequests.Folder(config.LogDir);
        return "Card moves: the scheduler is the only writer of card moves. Never git mv a card or commit a card "
            + "into another folder, and do not commit its last edit yourself. To finish the card (done/ or review/), "
            + "park it (needs-human, it stays in active/), send it back (waiting, to queue/) or stop: push your code "
            + "as the README says, edit the card in your worktree (Status, Questions and Result as the README says), "
            + "then hand the scheduler the whole card in one command: $t = Get-Content -Raw -Encoding UTF8 <the "
            + $"card's path>; $f = '{folder}\\' + (Get-Date -Format yyyyMMdd-HHmmss-fff) + '-slot{request.Slot}'; "
            + $"Set-Content -Encoding UTF8 -NoNewline -Path ($f + '.tmp') -Value ('Card: {request.Card.Name}' + "
            + "[char]10 + 'To: <done, review, active or queue>' + [char]10 + '---' + [char]10 + $t); Move-Item "
            + "($f + '.tmp') ($f + '.move'). The scheduler commits it, with your text, when your session ends, in "
            + "the order requests were written; you do not wait for it. Journal lines you still commit and push "
            + "yourself, to the card where it is.";
    }
}
