// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;

namespace Scheduler.Host.Moves;

/// <summary>
/// The scheduler as the only writer of card moves (card 660-B, roadmap item 2): the request files
/// in the moves folder applied one by one in name order through the answers worktree (MOVE-1). An
/// applied request goes to <c>done\</c>, one that cannot be applied to <c>failed\</c> with
/// <c>&lt;name&gt;.reason.txt</c>; one whose commit could not be pushed stays, and so do the ones
/// after it, for the next run. Design: <c>Scheduler.Core/Moves/card-moves.md</c>.
/// </summary>
public sealed class CardRequestStep
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly string[] Folders = ["queue", "active", "review", "done"];
    private readonly AnswersCommitter _committer;
    private readonly string _jobsDir;
    private readonly IClock _clock;
    private readonly ISchedulerLog? _log;

    /// <summary>A step over the moves folder <paramref name="folder"/>.</summary>
    /// <param name="committer">Commits through the answers worktree.</param>
    /// <param name="folder">The moves folder (<see cref="CardRequests.Folder"/> of the log folder).</param>
    /// <param name="jobsDir">The jobs folder in the repository, as <c>jobs</c>.</param>
    /// <param name="clock">The scheduler's clock.</param>
    /// <param name="log">The scheduler's log, or null.</param>
    public CardRequestStep(AnswersCommitter committer, string folder, string jobsDir, IClock clock, ISchedulerLog? log)
    {
        _committer = committer;
        Folder = folder;
        _jobsDir = jobsDir.TrimEnd('/', '\\').Replace('\\', '/');
        _clock = clock;
        _log = log;
    }

    /// <summary>The moves folder.</summary>
    public string Folder { get; }

    /// <summary>
    /// The scheduler's claim of <paramref name="card"/> for the worker it is starting in
    /// <paramref name="slot"/>: written as a request and applied with any before it, so the worker's
    /// first pull shows the card in active/. True when nothing is left waiting.
    /// </summary>
    public bool Claim(Card card, int slot)
    {
        Write(CardRequests.FileName(_clock.Now, slot), CardRequests.ClaimText(card.Name, slot));
        Run();
        return Pending().Count == 0;
    }

    /// <summary>995-B: writes the claim of <paramref name="card"/> for <paramref name="slot"/> as a request; returns its file name.</summary>
    public string WriteClaim(Card card, int slot)
    {
        var name = CardRequests.FileName(_clock.Now, slot);
        Write(name, CardRequests.ClaimText(card.Name, slot));
        return name;
    }

    /// <summary>
    /// 995-B: applies the waiting requests and says where the request <paramref name="name"/> went:
    /// <c>done\</c> is landed (its card is in active/ on main), <c>failed\</c> is failed (the card moved
    /// or was claimed already), and one still waiting is pending, to be applied again.
    /// </summary>
    public Scheduler.Core.Tick.ClaimState Apply(string name)
    {
        Run();
        return StateOf(name);
    }

    /// <summary>Writes a request as <c>.tmp</c> and renames it, so it is never read half written.</summary>
    public string Write(string fileName, string content)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, fileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Utf8);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Dynamic locks (DL-8 a and b, DL-9; card 1020-B): told of each worker's move request once it is
    /// filed - true when it is applied on main, false when it failed - so its card's locks can be released.
    /// Not told of claims, nor of a request whose commit could not be pushed (it is still waiting). Called
    /// on the thread that runs the step.
    /// </summary>
    public Action<CardRequest, bool>? Filed { get; set; }

    /// <summary>DL-9: true when a move request of <paramref name="card"/> (not a claim) is still waiting to be applied.</summary>
    public bool HasPending(string card)
    {
        foreach (var name in Pending())
        {
            try
            {
                var r = CardRequests.Parse(name, File.ReadAllText(Path.Combine(Folder, name), Utf8));
                if (r.Ok && !r.Claim && Text.Same(r.Card, card))
                {
                    return true;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // filed away while being read
            }
        }

        return false;
    }

    private void Report(CardRequest request, bool applied)
    {
        if (!request.Claim && request.Card.Length > 0)
        {
            Filed?.Invoke(request, applied);
        }
    }

    /// <summary>
    /// CM-12 (card 1237-B): the cards whose workers are still running, a snapshot the tick hands over on its own
    /// thread. Their move requests (not claims) wait until the run ends, so a finish is applied in the turn the
    /// session ends in (<see cref="Run(string)"/> with that card), never while the worker is still pushing.
    /// </summary>
    public IReadOnlySet<string> Running
    {
        get => _running;
        set => _running = value ?? throw new ArgumentNullException(nameof(value));
    }

    private volatile IReadOnlySet<string> _running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _heldNoted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CM-23 (cards 1237-B, 1300-B): the waiting <c>To: done</c> and <c>To: review</c> requests of <paramref name="card"/>
    /// send it back to wait on the planner instead of being settled: <c>To: queue</c> with
    /// <c>Status: waiting: planner (&lt;reason&gt;)</c> and a line in its Result saying what was asked for, so it is listed
    /// under the cards waiting on the planner and no worker takes it. Returns how many were rewritten.
    /// </summary>
    public int NotSettled(string card, string reason)
    {
        var rewritten = 0;
        foreach (var name in Pending())
        {
            var file = Path.Combine(Folder, name);
            try
            {
                var request = CardRequests.Parse(name, File.ReadAllText(file, Utf8));
                if (!request.Ok || request.Claim || !Text.Same(request.Card, card)
                    || request.To is not (CardFolder.Done or CardFolder.Review))
                {
                    continue;
                }

                var asked = request.To.ToString().ToLowerInvariant();
                var status = UnpushedWork.Waiting(reason);
                const string Lead = "finished, but ";
                var why = reason.StartsWith(Lead, StringComparison.Ordinal) ? reason[Lead.Length..] : reason;
                var line = $"the worker's finish asked for {asked}/, but {why}; it waits on the planner (CM-23).";
                var held = request with
                {
                    To = CardFolder.Queue,
                    Status = status,
                    Text = request.Text is { } text ? UnpushedWork.Journal(CardText.SetStatus(text, status), line, _clock.Now) : null,
                };
                File.WriteAllText(file + ".tmp", Scheduler.Host.Locks.AuditRunner.Text(held), Utf8);
                File.Move(file + ".tmp", file, overwrite: true);
                rewritten++;
                _log?.Write($"card move request {name}: {card} asked for {asked}/ but is not settled (CM-23): {reason}; it goes to queue/ to wait on the planner");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Write($"card move request {name} could not be read for CM-23: {e.Message}");
            }
        }

        return rewritten;
    }

    /// <summary>CM-23: true when a <c>To: done</c> or <c>To: review</c> request of <paramref name="card"/> is waiting.</summary>
    public bool HasFinish(string card)
    {
        foreach (var name in Pending())
        {
            try
            {
                var r = CardRequests.Parse(name, File.ReadAllText(Path.Combine(Folder, name), Utf8));
                if (r.Ok && !r.Claim && Text.Same(r.Card, card) && r.To is CardFolder.Done or CardFolder.Review)
                {
                    return true;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // filed away while being read
            }
        }

        return false;
    }

    /// <summary>The request files waiting, in the order they are applied.</summary>
    public IReadOnlyList<string> Pending() => Directory.Exists(Folder)
        ? CardRequests.Order(Directory.EnumerateFiles(Folder, "*" + CardRequests.Extension).Select(f => Path.GetFileName(f)!))
        : [];

    /// <summary>
    /// Applies every waiting request in order; returns how many were committed. 1190-B (MM-13): the requests waiting
    /// together go to the committer as one batch, one commit each in their order (CM-12) and one push; a card's second
    /// request starts the next batch, so a card's moves never pass each other. Each request has its own outcome: one
    /// that cannot be pushed is left waiting, in order, and the run ends there; the others are filed.
    /// </summary>
    public int Run() => Run(null);

    /// <summary>
    /// <see cref="Run()"/>, in the turn the run of <paramref name="ending"/> ends (CM-10, CM-12): that card's requests
    /// are applied though the tick still lists its worker as running; those of every other running card wait.
    /// </summary>
    public int Run(string? ending)
    {
        var applied = 0;
        while (true)
        {
            var (batch, more) = Gather(ending);
            if (batch.Count == 0)
            {
                return applied;
            }

            var results = _committer.UpdateCards([.. batch.Select(UpdateOf)]);
            var held = false;
            for (var i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                var r = results[i];
                if (item.NotOnMain)
                {
                    FileAway(item.Name, $"{item.Request.Card} is not on main");
                    Report(item.Request, false);
                }
                else if (r.Ok || item.Unchanged)
                {
                    applied += r.Ok ? 1 : 0;
                    FileAway(item.Name, "");
                    Report(item.Request, true);
                }
                else if (item.PlanError.Length > 0)
                {
                    FileAway(item.Name, item.PlanError);
                    Report(item.Request, false);
                }
                else
                {
                    _log?.Write($"card move request {item.Name} not committed, tried again next time: {r.Error}");
                    held = true;
                }
            }

            if (held || !more)
            {
                return applied;
            }
        }
    }

    /// <summary>
    /// 995-B: where the request <paramref name="name"/> went: <c>done\</c> is landed, <c>failed\</c> is failed, and
    /// one still waiting is pending.
    /// </summary>
    public Scheduler.Core.Tick.ClaimState StateOf(string name)
    {
        if (File.Exists(Path.Combine(Folder, name)))
        {
            return Scheduler.Core.Tick.ClaimState.Pending;
        }

        return File.Exists(Path.Combine(Folder, "failed", name))
            ? Scheduler.Core.Tick.ClaimState.Failed
            : Scheduler.Core.Tick.ClaimState.Landed;
    }

    /// <summary>A waiting request on its way through a batch, and how its plan went.</summary>
    private sealed class Item(string name, CardRequest request)
    {
        public string Name { get; } = name;

        public CardRequest Request { get; } = request;

        public bool NotOnMain { get; set; }

        public bool Unchanged { get; set; }

        public string PlanError { get; set; } = "";
    }

    /// <summary>
    /// The next batch, in order: every waiting request up to one still being written or a card's second request
    /// (more is true then, for the next batch). Requests that cannot be read as requests are filed away.
    /// </summary>
    private (List<Item> Batch, bool More) Gather(string? ending)
    {
        var batch = new List<Item>();
        var cards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var running = _running;
        foreach (var name in Pending())
        {
            string content;
            try
            {
                content = File.ReadAllText(Path.Combine(Folder, name), Utf8);
            }
            catch (IOException)
            {
                return (batch, false); // still being written or held: keep the order, try again next run
            }

            var request = CardRequests.Parse(name, content);
            if (!request.Ok)
            {
                FileAway(name, request.Error);
                continue;
            }

            // CM-12: a running worker's request waits for its run to end; claims never wait.
            if (!request.Claim && running.Contains(request.Card) && !Text.Same(request.Card, ending ?? ""))
            {
                if (_heldNoted.Add(name))
                {
                    _log?.Write($"card move request {name} waits: {request.Card}'s worker is still running (CM-12)");
                }

                continue;
            }

            if (!cards.Add(request.Card))
            {
                return (batch, true);
            }

            batch.Add(new Item(name, request));
        }

        return (batch, false);
    }

    /// <summary>The committer's update for <paramref name="item"/>; its outcome is noted on the item each try.</summary>
    private CardUpdate UpdateOf(Item item) => new(
        item.Request.Card,
        () =>
        {
            item.NotOnMain = false;
            item.Unchanged = false;
            item.PlanError = "";
            var path = Find(item.Request.Card);
            item.NotOnMain = path is null;
            return path;
        },
        main =>
        {
            var path = Find(item.Request.Card)!;
            var card = CardFiles.Parse(_jobsDir, path, main);
            if (card is null)
            {
                item.PlanError = $"{path} is not a card";
                return CardEdit.Failed(item.PlanError);
            }

            var e = CardRequests.Plan(card, main, item.Request, _clock.Now);
            if (!e.Ok)
            {
                item.PlanError = e.Error;
                return e;
            }

            if (!e.Moves && Same(e.Text, main))
            {
                item.Unchanged = true;
                return CardEdit.Failed("nothing to change");
            }

            return e;
        },
        ReplayLocalEdits: false);

    /// <summary>The card's path on main in the answers worktree, as the batch has it so far, or null.</summary>
    private string? Find(string cardName)
    {
        var dir = _committer.AnswersDir;
        return Folders.Select(f => $"{_jobsDir}/{f}/{cardName}.md")
            .FirstOrDefault(p => File.Exists(Path.Combine(dir, p.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>Moves a request to done\ (no reason) or failed\ with its reason.</summary>
    private void FileAway(string name, string reason)
    {
        var target = Path.Combine(Folder, reason.Length == 0 ? "done" : "failed");
        Directory.CreateDirectory(target);
        var dest = Path.Combine(target, name);
        try
        {
            File.Move(Path.Combine(Folder, name), dest, overwrite: true);
            if (reason.Length > 0)
            {
                File.WriteAllText(dest[..^CardRequests.Extension.Length] + ".reason.txt", reason + Environment.NewLine, Utf8);
                _log?.Write($"card move request {name} failed: {reason}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Write($"card move request {name} could not be filed: {e.Message}");
        }
    }

    private static bool Same(string a, string b) => a.Replace("\r\n", "\n").TrimEnd() == b.Replace("\r\n", "\n").TrimEnd();
}
