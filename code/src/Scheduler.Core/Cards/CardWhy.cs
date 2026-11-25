// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// Why a card is listed under "Waiting on you" (CARD-13): a faithful port of the three-way choice
/// in <c>Read-Cards</c> (schedule.ps1 L631-634). The answer says both what to show and where it
/// came from, because the page styles the two sources differently.
/// </summary>
public static class CardWhy
{
    /// <summary>
    /// The reason <paramref name="folder"/>'s card with <paramref name="status"/> waits on the
    /// developer, and <c>status</c> or <c>result</c> for where it came from: the text after
    /// <c>needs-human:</c> or <c>review:</c>; else, for a card in queue/ or active/ that waits on
    /// the developer, the Status without its <c>waiting:</c>; else, for a review card, the newest
    /// entry of <paramref name="resultLines"/> (the lines of its <c>## Result</c>, CARD-14).
    /// Both are empty when none of the three applies.
    /// </summary>
    public static (string Why, string WhyFrom) For(
        CardFolder folder,
        string status,
        IReadOnlyList<string> resultLines)
    {
        if (Text.Match(status, @"^(?:needs-human|review)\s*:\s*(.+)$") is { Success: true } parked)
        {
            return (parked.Groups[1].Value, "status");
        }

        if (folder is CardFolder.Queue or CardFolder.Active
            && Text.IsMatch(status, CardWaits.DeveloperPattern))
        {
            return (Text.Match(status, @"^waiting:\s*(.*)$") is { Success: true } waiting
                ? waiting.Groups[1].Value
                : status, "status");
        }

        if (folder == CardFolder.Review)
        {
            // L633: no journal entry leaves WhyFrom unset as well as Why empty.
            var journal = CardJournal.Latest(resultLines);
            if (journal.Length > 0)
            {
                return (journal, "result");
            }
        }

        return ("", "");
    }
}
