using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;

namespace Scheduler.Host.Locks;

/// <summary>
/// Dynamic locks in the host (design <c>dynamic-locks.md</c>, section 6, card 1020-B): the lock step.
/// It asks the open waits again (DL-12: a release since the last step may grant them, DL-13: a wait may
/// have run out), then answers the <c>.lock</c> requests in the moves folder in name order, each checked
/// against the card its slot runs (DL-22), through Core's <see cref="LockArbiter"/>. It writes the table
/// first and then each reply (<c>moves\replies\&lt;name&gt;.reply</c>, tmp then rename as CM-1), and files each
/// request away to <c>moves\done\</c>, or to <c>moves\failed\</c> with its reason when it was refused.
/// </summary>
public sealed class LockRequestStep
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Func<LockTable> _read;
    private readonly Action<LockTable> _save;
    private readonly IClock _clock;
    private readonly ISchedulerLog? _log;

    /// <summary>A step over the moves folder <paramref name="folder"/>.</summary>
    /// <param name="folder">The moves folder (<see cref="Scheduler.Core.Moves.CardRequests.Folder"/> of the log folder).</param>
    /// <param name="read">Reads the repository's lock table.</param>
    /// <param name="save">Writes it (tmp then rename, DL-20).</param>
    /// <param name="clock">The scheduler's clock.</param>
    /// <param name="log">The scheduler's log, or null.</param>
    public LockRequestStep(string folder, Func<LockTable> read, Action<LockTable> save, IClock clock, ISchedulerLog? log)
    {
        Folder = folder;
        _read = read;
        _save = save;
        _clock = clock;
        _log = log;
    }

    /// <summary>The moves folder.</summary>
    public string Folder { get; }

    /// <summary>The replies folder (<c>moves\replies\</c>).</summary>
    public string Replies => Path.Combine(Folder, LockRequests.RepliesFolder);

    /// <summary>The <c>.lock</c> files waiting, in the order they are answered.</summary>
    public IReadOnlyList<string> Pending() => Directory.Exists(Folder)
        ? LockRequests.Order(Directory.EnumerateFiles(Folder, "*" + LockRequests.Extension).Select(f => Path.GetFileName(f)!))
        : [];

    /// <summary>
    /// One pass: open waits asked again, then every waiting request answered. <paramref name="slotCard"/>
    /// gives the card a slot runs (null for none), <paramref name="warnAt"/> a running card's warn time
    /// (DL-13). Returns the decisions, in the order they were made.
    /// </summary>
    public IReadOnlyList<LockDecision> Run(LockRules rules, Func<int, string?> slotCard, Func<string, DateTimeOffset?> warnAt)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(slotCard);
        ArgumentNullException.ThrowIfNull(warnAt);
        var pending = Pending();
        var before = _read();
        if (pending.Count == 0 && !before.Holding.Any(c => c.Open is not null))
        {
            return [];
        }

        var now = _clock.Now;
        var decisions = new List<LockDecision>();
        var replies = new Dictionary<string, string>(StringComparer.Ordinal);
        var filed = new List<(string Name, string Reason)>();

        // DL-12, DL-13: the open waits first, oldest card first, so a release since the last step grants them.
        var (table, rechecked) = LockArbiter.Recheck(before, now, rules);
        foreach (var d in rechecked)
        {
            decisions.Add(d);
            replies[d.FileName] = Reply(d);
            if (d.Answer != LockAnswer.Waiting)
            {
                Write(d, "");
            }
        }

        foreach (var name in pending)
        {
            string content;
            try
            {
                content = File.ReadAllText(Path.Combine(Folder, name), Utf8);
            }
            catch (IOException)
            {
                break;   // still being written or held: keep the order, answer it next step
            }

            var parsed = LockRequests.Parse(name, content);
            var request = LockRequests.Verify(parsed, parsed.Slot is { } slot ? slotCard(slot) : null);   // DL-22
            var d = LockArbiter.Ask(table, request, now, rules, request.Ok ? warnAt(request.Card) : null);
            table = d.Table;
            decisions.Add(d);
            var text = Reply(d);
            replies[name] = text;
            replies[d.FileName] = text;   // merged into the card's open request: that one's reply too
            filed.Add((name, d.Answer == LockAnswer.Refused ? d.Reason : ""));
            Write(d, request.Why);
        }

        // DL-20: the table first, then the replies, so a worker that reads its reply finds the table agreeing.
        if (LockTableText.Write(table) != LockTableText.Write(before))
        {
            _save(table);
        }

        foreach (var (request, text) in replies)
        {
            WriteReply(request, text);
        }

        foreach (var (name, reason) in filed)
        {
            FileAway(name, reason);
        }

        return decisions;
    }

    /// <summary>The reply's path for a request file name.</summary>
    public string ReplyPath(string requestFileName) => Path.Combine(Replies, LockRequests.ReplyName(requestFileName));

    // LockReply: the time a waiting reply gives up at is shown in this PC's local time.
    private static string Reply(LockDecision d) =>
        LockReply.Text(d.GivesUpAt is { } at ? d with { GivesUpAt = at.ToLocalTime() } : d);

    private void Write(LockDecision d, string why)
    {
        var card = d.Card.Length > 0 ? d.Card : "?";
        var reason = why.Length > 0 ? $" ({why})" : "";
        _log?.Write($"locks: {card} {d.FileName}: {Reply(d)}{reason}");   // DL-26
    }

    private void WriteReply(string requestFileName, string text)
    {
        try
        {
            Directory.CreateDirectory(Replies);
            var path = ReplyPath(requestFileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text + "\n", Utf8);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Write($"locks: reply to {requestFileName} could not be written: {e.Message}");
        }
    }

    private void FileAway(string name, string reason)
    {
        var target = Path.Combine(Folder, reason.Length == 0 ? "done" : "failed");
        try
        {
            Directory.CreateDirectory(target);
            var dest = Path.Combine(target, name);
            File.Move(Path.Combine(Folder, name), dest, overwrite: true);
            if (reason.Length > 0)
            {
                File.WriteAllText(dest[..^LockRequests.Extension.Length] + ".reason.txt", reason + Environment.NewLine, Utf8);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Write($"locks: request {name} could not be filed: {e.Message}");
        }
    }
}
