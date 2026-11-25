// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;
using Scheduler.Host.Moves;

namespace Scheduler.Host.Processes;

/// <summary>
/// WT-P2 (question 4): with no slot to disable, a worker stopped or killed from the page leaves its
/// card in <c>active/</c> with <c>Status: waiting: you (stopped from the page)</c>, which the page
/// lists under Waiting on you with Resume (MOVE-7) and the planner leaves alone (DISP-10) until
/// the developer resumes it. Done once the worker is gone, so the card text is the worker's last.
/// </summary>
public sealed class StoppedCardHold(AnswersCommitter committer, IClock clock, ISchedulerLog log)
{
    public const string Status = "waiting: you (stopped from the page)";

    /// <summary>True for a worker the developer stopped or killed from the page.</summary>
    public static bool Applies(RunningWorker worker) =>
        worker.StopWhy is "stopped from the page" or "killed from the page";

    /// <summary>
    /// Sets the card waiting, but only while main still has it in <c>active/</c> and not already
    /// finished by the worker (its Status is still a working one).
    /// </summary>
    public bool Hold(Card card)
    {
        if (card.Folder != CardFolder.Active)
        {
            return false;
        }

        CardEdit? last = null;
        var result = committer.UpdateCard(card.Path, text =>
        {
            last = Held(text)
                ? CardMoves.Update(card, text, Status, CardFolder.Active,
                    "stopped from the page; Resume to carry on.", $"jobs: wait {card.Name} (stopped from the page)", clock.Now)
                : CardEdit.Failed($"{card.Id} is no longer a working card");
            return last;
        });
        if (!result.Ok)
        {
            log.Write($"{card.Name}: not set waiting after the stop from the page ({result.Error})");
            return false;
        }

        log.Write($"{card.Name}: waits on you after the stop from the page");
        return true;
    }

    // Only a card still marked as in progress is held: one the worker finished (done, review,
    // needs-human, waiting) keeps what it set.
    private static bool Held(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^Status:[ \t]*(.*?)[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return m.Success && !Text.IsMatch(m.Groups[1].Value, "^(done|review|needs-human|waiting)");
    }
}
