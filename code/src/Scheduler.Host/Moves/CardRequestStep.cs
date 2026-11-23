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
        if (File.Exists(Path.Combine(Folder, name)))
        {
            return Scheduler.Core.Tick.ClaimState.Pending;
        }

        return File.Exists(Path.Combine(Folder, "failed", name))
            ? Scheduler.Core.Tick.ClaimState.Failed
            : Scheduler.Core.Tick.ClaimState.Landed;
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

    /// <summary>The request files waiting, in the order they are applied.</summary>
    public IReadOnlyList<string> Pending() => Directory.Exists(Folder)
        ? CardRequests.Order(Directory.EnumerateFiles(Folder, "*" + CardRequests.Extension).Select(f => Path.GetFileName(f)!))
        : [];

    /// <summary>Applies every waiting request in order; returns how many were committed.</summary>
    public int Run()
    {
        var applied = 0;
        foreach (var name in Pending())
        {
            string content;
            try
            {
                content = File.ReadAllText(Path.Combine(Folder, name), Utf8);
            }
            catch (IOException)
            {
                return applied; // still being written or held: keep the order, try again next run
            }

            var request = CardRequests.Parse(name, content);
            if (!request.Ok)
            {
                FileAway(name, request.Error);
                continue;
            }

            var path = Find(request.Card);
            if (path is null)
            {
                FileAway(name, $"{request.Card} is not on main");
                Report(request, false);
                continue;
            }

            var planError = "";
            var unchanged = false;
            var r = _committer.UpdateCard(path, main =>
            {
                var card = CardFiles.Parse(_jobsDir, path, main);
                if (card is null)
                {
                    planError = $"{path} is not a card";
                    return CardEdit.Failed(planError);
                }

                var e = CardRequests.Plan(card, main, request, _clock.Now);
                if (!e.Ok)
                {
                    planError = e.Error;
                    return e;
                }

                if (!e.Moves && Same(e.Text, main))
                {
                    unchanged = true;
                    return CardEdit.Failed("nothing to change");
                }

                return e;
            }, replayLocalEdits: false);

            if (r.Ok || unchanged)
            {
                applied += r.Ok ? 1 : 0;
                FileAway(name, "");
                Report(request, true);
            }
            else if (planError.Length > 0)
            {
                FileAway(name, planError);
                Report(request, false);
            }
            else
            {
                _log?.Write($"card move request {name} not committed, tried again next time: {r.Error}");
                return applied;
            }
        }

        return applied;
    }

    /// <summary>The card's path on main (in the answers worktree, reset to main), or null.</summary>
    private string? Find(string cardName)
    {
        var dir = _committer.AnswersTree();
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
