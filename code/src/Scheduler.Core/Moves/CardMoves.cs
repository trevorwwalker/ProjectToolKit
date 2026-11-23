using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Moves;

/// <summary>
/// The card actions of the page (A10), pure: each takes the card as the scheduler last read it and
/// main's current text of it (in the answers worktree, after Host has replayed the developer's own
/// edits, MOVE-4), and returns the <see cref="CardEdit"/> for Host (250-B) to commit and push.
/// </summary>
public static class CardMoves
{
    /// <summary>The longest note kept with an answer (L3897).</summary>
    public const int MaxNote = 500;

    /// <summary>
    /// MOVE-2 (Answer-Card L1076), with the developer's decision of 2026-11-18: question
    /// <paramref name="question"/> gets <c> -&gt; **label** (developer, yyyy-MM-dd HH:mm)</c> and the
    /// optional note line (white space runs made one space, cut to 500 characters, as the POST
    /// handler does at L3896-3897). The card keeps its folder and Status while it has open
    /// questions. When this was its last open question it goes to the destination needing the most
    /// work among the answers <em>this round</em> chose (active over queue over done; an answer given
    /// before the card last began to wait was acted on already, 948-A; the destination of the answer
    /// just given counts whatever its stamp, 1057-B) with Status <c>done</c> for
    /// done/ and <c>ready</c> otherwise; with no destination at all, an
    /// active card becomes <c>ready</c>. An answered question, or a choice the question does not
    /// have, is an error.
    /// </summary>
    /// <param name="card">The card as the page showed it.</param>
    /// <param name="text">Main's current text of the card.</param>
    /// <param name="question">The question's number (1 for Q1).</param>
    /// <param name="choice">The chosen answer's index among the question's choices.</param>
    /// <param name="note">The developer's note, or empty.</param>
    /// <param name="now">The page's clock.</param>
    public static CardEdit Answer(Card card, string text, int question, int choice, string note, DateTimeOffset now)
    {
        var qid = $"Q{question}";
        var q = card.Questions.FirstOrDefault(x => x.Number == question);
        if (q is null || choice < 0 || choice >= q.Choices.Count)
        {
            return CardEdit.Failed($"no open question {qid} on {card.Id}");
        }

        var option = q.Choices[choice];
        note = Regex.Replace(note ?? "", @"\s+", " ").Trim();
        if (note.Length > MaxNote)
        {
            note = note[..MaxNote];
        }

        var lines = CardText.Split(text);
        var at = -1;
        var inQuestions = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (Text.IsMatch(lines[i], "^## "))
            {
                inQuestions = Text.IsMatch(lines[i], "^## Questions");
                continue;
            }

            if (inQuestions && CardQuestions.ParseBlock(lines, i, out var end) is { } p)
            {
                if (p.Number == question)
                {
                    // 820-A: the answer goes after the closing bracket, which is on the block's
                    // last line, and the note goes on the line after that.
                    at = end;
                    break;
                }

                i = end;
            }
        }

        if (at < 0)
        {
            return CardEdit.Failed($"{qid} on {card.Id} is already answered");
        }

        lines[at] = lines[at].TrimEnd() + $" -> **{option.Label}** (developer, {CardText.Stamp(now)})";
        if (note.Length > 0)
        {
            lines.Insert(at + 1, $"  Note: {note}");
        }

        var (open, destinations) = Answers(lines, at);
        var target = card.Folder;
        var status = "";
        if (open == 0)
        {
            if (destinations.Count > 0)
            {
                target = destinations.MaxBy(Work);
                status = target == CardFolder.Done ? "done" : "ready";
            }
            else if (card.Folder == CardFolder.Active)
            {
                status = "ready";
            }
        }

        if (status.Length > 0)
        {
            CardText.SetStatus(lines, status);
        }

        var moved = target != card.Folder;
        var message = $"jobs: answer {card.Id} {qid}: {option.Label}{(moved ? $" - to {FolderName(target)}" : "")}".Replace('"', '\'');
        var log = $"answered from the page: {card.Id} {qid}: {option.Label}" +
            (note.Length > 0 ? $" (note: {note})" : "") +
            (moved ? $"; card moved to {FolderName(target)}" : status.Length > 0 ? $"; card set {status}" : "");
        return Edit(card, target, string.Join(CardText.Eol(text), lines), status, message, log, clearsRetries: false);
    }

    /// <summary>
    /// The text part of Update-Card (L1204-1254), after Host's replay of the developer's edits
    /// (MOVE-4): Status set (MOVE-3), the journal line added (MOVE-5), and the card moved to
    /// <paramref name="target"/> when that is another folder.
    /// </summary>
    public static CardEdit Update(Card card, string text, string status, CardFolder target, string line, string message, DateTimeOffset now) =>
        Update(card, text, status, target, line, message, now, clearsRetries: false);

    /// <summary>
    /// MOVE-6 (Done-Card L1521): a review card gets Status <c>done</c>, goes to done/ with the
    /// journal line "reviewed, done (from the page)."; any other card is an error.
    /// </summary>
    public static CardEdit Done(Card card, string text, DateTimeOffset now)
    {
        if (card.Folder != CardFolder.Review)
        {
            return CardEdit.Failed($"{card.Id} is not in review");
        }

        return Update(card, text, "done", CardFolder.Done, "reviewed, done (from the page).", $"jobs: done {card.Name} (reviewed from the page)", now);
    }

    /// <summary>
    /// DL-19 (design <c>dynamic-locks.md</c>; developer, 994-D Q5): a card the audit of main found a stray
    /// for does not reach done unseen. Its request To: done becomes To: review with Status
    /// <c>review: changed &lt;path&gt; without its lock</c>, in its text when it has one, else in the
    /// request's Status. Any other request, or no <paramref name="path"/>, is returned as it is.
    /// </summary>
    /// <param name="request">The card's move request.</param>
    /// <param name="path">The first path the card changed without holding it.</param>
    public static CardRequest Stray(CardRequest request, string path)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.To != CardFolder.Done || string.IsNullOrWhiteSpace(path))
        {
            return request;
        }

        var status = $"review: changed {path.Trim()} without its lock";
        return request.Text is { } text
            ? request with { To = CardFolder.Review, Text = CardText.SetStatus(text, status) }
            : request with { To = CardFolder.Review, Status = status };
    }

    /// <summary>
    /// MOVE-7 (Resume-Card L1530): a review card goes back to active/ ready ("sent back from
    /// review"), an active needs-human card becomes ready ("resumed"), and a card waiting on the
    /// developer becomes ready where it is. The retry count is cleared once pushed, except for a
    /// queue card (Spec C9). Any other card is an error.
    /// </summary>
    public static CardEdit Resume(Card card, string text, DateTimeOffset now)
    {
        var waits = card.Folder == CardFolder.Review ||
            (card.Folder == CardFolder.Active && Text.IsMatch(card.Status, "^needs-human")) ||
            CardWaits.OnDeveloper(card);
        if (!waits)
        {
            return CardEdit.Failed($"{card.Id} is not waiting on you");
        }

        if (card.Folder == CardFolder.Queue)
        {
            // Spec C9: today's behaviour, kept until the developer decides (003-D review).
            return Update(card, text, "ready", CardFolder.Queue, "set ready from the page; read any ## Dev section first.", $"jobs: ready {card.Name} (from the page)", now, clearsRetries: false);
        }

        var how = card.Folder == CardFolder.Review ? "sent back from review" : "resumed";
        return Update(card, text, "ready", CardFolder.Active, $"{how} from the page; read any ## Dev section first.", $"jobs: {how.Replace(" from review", "", StringComparison.Ordinal)} {card.Name} (from the page)", now, clearsRetries: true);
    }

    /// <summary>After <paramref name="edit"/> is pushed: clears the card's retry count when it says so (MOVE-7, L1541).</summary>
    public static void Committed(CardEdit edit, IDictionary<string, int> retries)
    {
        if (edit.Ok && edit.ClearsRetries)
        {
            retries.Remove(edit.CardId);
        }
    }

    /// <summary>
    /// MOVE-8 (<c>/retry</c>, L3950-3953): removes the card's retry entry; true when there was one
    /// (the scheduler then logs "retry from the page: &lt;id&gt;'s run count cleared" and ticks).
    /// The state's Resume, which clears every entry, is <see cref="Health.HealthCheck.Resume"/>.
    /// </summary>
    public static bool Retry(IDictionary<string, int> retries, string cardId) =>
        !string.IsNullOrEmpty(cardId) && retries.Remove(cardId);

    /// <summary>
    /// 1099-C: a card edit that changes nothing itself - the card stays in its folder with its Status -
    /// for the caller to add the developer's note to (<c>Save note</c>).
    /// </summary>
    public static CardEdit Note(Card card, string text) =>
        Edit(card, card.Folder, text, "", $"jobs: note {card.Name} (from the page)", $"note saved from the page on {card.Id}", false);

    private static CardEdit Update(Card card, string text, string status, CardFolder target, string line, string message, DateTimeOffset now, bool clearsRetries)
    {
        var lines = CardText.Split(text);
        CardText.SetStatus(lines, status);
        CardText.AddJournal(lines, line, now);
        return Edit(card, target, string.Join(CardText.Eol(text), lines), status, message.Replace('"', '\''), message, clearsRetries);
    }

    /// <summary>
    /// The open questions under <c>## Questions</c>, and the destinations of the answers the
    /// developer gave <em>this round</em>: the answer label (<c>-&gt; **label**</c>) looked up among
    /// the question's own choices. A question is read as a block, the same way
    /// <see cref="Answer"/> finds it (820-A), so one written over several lines is counted once and
    /// its answer is found wherever its bracket ended.
    /// <para>
    /// MOVE-2 as decided for 948-A: an answer given before the card last began to wait was already
    /// answered and acted on, so its destination is not this round's; only answers stamped after the
    /// round started count (see <see cref="RoundStart"/>). Every question still counts as open or
    /// answered, whichever round its answer belongs to.
    /// </para>
    /// <para>
    /// 1057-B: <paramref name="justAnswered"/> is the line the answer <see cref="Answer"/> has
    /// <em>now</em> written (the block's last line), or -1 when the caller is only reading. The
    /// destination of that question always counts - it is the developer's choice for this click, and
    /// the card must go where he sent it whatever its stamp reads - and the rest count only from this
    /// round on, so an old already-acted-on answer cannot outrank the new one.
    /// </para>
    /// </summary>
    private static (int Open, List<CardFolder> Destinations) Answers(List<string> lines, int justAnswered)
    {
        var open = 0;
        var destinations = new List<CardFolder>();
        var roundStart = RoundStart(lines);
        var inQuestions = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (Text.IsMatch(lines[i], "^## "))
            {
                inQuestions = Text.IsMatch(lines[i], "^## Questions");
            }
            else if (inQuestions)
            {
                if (CardQuestions.ParseBlock(lines, i, out var end) is not null)
                {
                    open++;
                    i = end;
                }
                else if (CardQuestions.ParseAnswered(lines, i, out end, out var label) is { } asked)
                {
                    if (asked.Choices.FirstOrDefault(c => Text.Same(c.Label, label)) is { Destination: { } to } &&
                        (end == justAnswered || GivenThisRound(lines, i, end, roundStart)))
                    {
                        destinations.Add(to);
                    }

                    i = end;
                }
            }
        }

        return (open, destinations);
    }

    /// <summary>
    /// MOVE-2 (948-A): when this round of questions began. A worker journals the card just before it
    /// parks it as needs-human or puts it up for review, and the page stamps an answer with the clock
    /// of the sitting, so the newest dated line of the card's <c>## Result</c> that a worker wrote
    /// (the page's own <c>developer:</c> lines mark the card leaving a waiting state, not a handover)
    /// is the start of the round: answers given after it are the ones the developer is answering now.
    /// A card with no dated journal line has no round start, and every answer counts as before.
    /// </summary>
    private static string? RoundStart(List<string> lines)
    {
        var result = new List<string>();
        var inResult = false;
        foreach (var line in lines)
        {
            if (Text.IsMatch(line, "^## "))
            {
                inResult = Text.IsMatch(line, "^## Result");
            }
            else if (inResult)
            {
                result.Add(line);
            }
        }

        return CardJournal.LatestKey(result, skipPageLines: true);
    }

    /// <summary>
    /// Whether the answered question whose block runs from <paramref name="start"/> to
    /// <paramref name="end"/> was answered at or after <paramref name="roundStart"/>. An answer with
    /// no time to go by - the page always writes <c>(developer, yyyy-MM-dd HH:mm)</c>, but a hand
    /// written one may carry no stamp or no clock - counts, as it always did.
    /// <para>
    /// 1057-B: the stamp is read even when text follows the clock inside the parentheses, as it does
    /// when the developer's note is written there (<c>(developer, 2026-11-21 19:40: "...)</c>) rather
    /// than on the <c>Note:</c> line the page adds. Requiring the closing bracket straight after the
    /// minutes lost the time, the answer was read as unstamped, and an old answer from before the
    /// card's last move outranked the fresh one.
    /// </para>
    /// </summary>
    private static bool GivenThisRound(List<string> lines, int start, int end, string? roundStart)
    {
        if (roundStart is null)
        {
            return true;
        }

        var block = string.Join(" ", lines[start..(end + 1)].Select(static l => l.Trim()));
        if (Text.Match(block, @"\(developer,\s*(\d{4}-\d{2}-\d{2})(?:[ T](\d{1,2}):(\d{2}))?") is not { Success: true } m)
        {
            return true;
        }

        var stamp = m.Groups[1].Value;
        if (!m.Groups[2].Success)
        {
            return true;
        }

        var hour = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        stamp += " " + hour.ToString("D2", CultureInfo.InvariantCulture) + ":" + m.Groups[3].Value;
        return Text.SortComparer.Compare(stamp, roundStart) >= 0;
    }

    /// <summary>How much work a destination means: active over queue over done.</summary>
    private static int Work(CardFolder folder) => folder switch
    {
        CardFolder.Active => 3,
        CardFolder.Queue => 2,
        CardFolder.Done => 1,
        _ => 0,
    };

    private static string FolderName(CardFolder folder) => folder.ToString().ToLowerInvariant();

    private static CardEdit Edit(Card card, CardFolder target, string text, string status, string message, string log, bool clearsRetries)
    {
        var newPath = card.Path;
        if (target != card.Folder)
        {
            var folderDir = card.Path[..Math.Max(card.Path.LastIndexOf('/'), 0)];
            var jobsDir = folderDir[..Math.Max(folderDir.LastIndexOf('/'), 0)];
            newPath = $"{jobsDir}/{FolderName(target)}/{card.Name}.md";
        }

        return new CardEdit
        {
            CardId = card.Id,
            Path = card.Path,
            NewPath = newPath,
            Folder = target,
            Text = text,
            Status = status,
            Message = message,
            Log = log,
            ClearsRetries = clearsRetries,
        };
    }
}
