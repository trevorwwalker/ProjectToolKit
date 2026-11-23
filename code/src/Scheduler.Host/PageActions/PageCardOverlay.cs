using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;

namespace Scheduler.Host.PageActions;

/// <summary>
/// 990-B: the scheduler's own view moves with a page action. When an answer, Done or Send back has
/// been committed, the tick's card list still holds the card where it was until its next card read
/// (2-15 s, 987-B), so a snapshot built in between put the answered or finished card back on the
/// page. The page action notes each committed edit here, and the snapshot lays the moved card over
/// the tick's list - its new folder, Status and open questions - until the tick's own read agrees
/// (or <see cref="Keep"/> has passed, so a move main never shows cannot hold the page for ever).
/// 1084-B: also until the tick's read shows the card other than it was when the action started - main
/// has moved on (a planner batch, a worker's move, a status commit), and main, not the page's edit, is
/// then what the page shows; before that a later move showed the card twice, or with the edit's Status.
/// Written on the page action thread, read on the scheduler thread: everything is under one lock.
/// </summary>
public sealed class PageCardOverlay(string jobsDir, IClock clock)
{
    /// <summary>How long a landed move is laid over the tick's list at most.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);

    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    private readonly object _gate = new();
    private readonly List<Landing> _landed = [];

    /// <summary>How many landed moves are still laid over the tick's list.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _landed.Count;
            }
        }
    }

    /// <summary>Notes a committed card edit: from now on the snapshot shows the card as it left it.</summary>
    /// <param name="edit">The committed edit.</param>
    /// <param name="before">
    /// The tick's card the action started from (1084-B): once the tick's list no longer shows it so, the tick
    /// has read a newer main and the landing is forgotten. Null keeps only the old rule (the read agrees).
    /// </param>
    public void Landed(CardEdit edit, Card? before = null)
    {
        if (!edit.Ok || edit.NewPath.Length == 0)
        {
            return;
        }

        var card = CardFiles.Parse(jobsDir, edit.NewPath, edit.Text);
        lock (_gate)
        {
            // A second click on the same card follows the first: it starts where that one left it.
            var from = edit.Path;
            var earlier = _landed.FindIndex(l => Paths.Equals(l.To, edit.Path));
            if (earlier >= 0)
            {
                from = _landed[earlier].From;
                before = _landed[earlier].Before;
                _landed.RemoveAt(earlier);
            }

            _landed.Add(new Landing(from, edit.NewPath, card, before is not null && Paths.Equals(before.Path, from) ? before : null, clock.Now));
        }
    }

    /// <summary>
    /// The tick's cards with every landed move laid over them. A move the tick's list already shows, one
    /// it has read past (1084-B), or one older than <see cref="Keep"/>, is forgotten here.
    /// </summary>
    public IReadOnlyList<Card> Apply(IReadOnlyList<Card> cards)
    {
        lock (_gate)
        {
            var now = clock.Now;
            _landed.RemoveAll(l => now - l.At > Keep || Agrees(cards, l) || ReadPast(cards, l));
            if (_landed.Count == 0)
            {
                return cards;
            }

            var hide = new HashSet<string>(Paths);
            foreach (var l in _landed)
            {
                hide.Add(l.From);
                hide.Add(l.To);
            }

            var list = cards.Where(c => !hide.Contains(c.Path)).ToList();
            list.AddRange(_landed.Where(l => l.Card is not null).Select(l => l.Card!));
            return list;
        }
    }

    // The tick's read shows the move: nothing left at the old path, and the card at the new one with
    // the Status and open questions the edit gave it.
    private static bool Agrees(IReadOnlyList<Card> cards, Landing l)
    {
        if (!Paths.Equals(l.From, l.To) && cards.Any(c => Paths.Equals(c.Path, l.From)))
        {
            return false;
        }

        var now = cards.FirstOrDefault(c => Paths.Equals(c.Path, l.To));
        if (l.Card is null || now is null)
        {
            return l.Card is null;
        }

        return string.Equals(now.Status, l.Card.Status, StringComparison.Ordinal)
            && now.Questions.Select(q => q.Number).SequenceEqual(l.Card.Questions.Select(q => q.Number));
    }

    // 1084-B: the tick's list no longer shows the card as the action found it, so the tick has read main
    // since: what it read is newer than the landing, even where it is not what the edit made.
    private static bool ReadPast(IReadOnlyList<Card> cards, Landing l)
    {
        if (l.Before is not { } before)
        {
            return false;
        }

        var now = cards.FirstOrDefault(c => Paths.Equals(c.Path, l.From));
        return now is null
            || !string.Equals(now.Status, before.Status, StringComparison.Ordinal)
            || !now.Questions.Select(q => q.Number).SequenceEqual(before.Questions.Select(q => q.Number));
    }

    private sealed record Landing(string From, string To, Card? Card, Card? Before, DateTimeOffset At);
}
