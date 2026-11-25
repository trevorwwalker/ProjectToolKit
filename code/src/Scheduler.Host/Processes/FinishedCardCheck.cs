// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Core.PageModel;

namespace Scheduler.Host.Processes;

/// <summary>
/// Catches a worker that finishes (<c>LANE-RESULT: done</c> or <c>review</c>) and moves its card,
/// but whose card text is identical to the last text seen in <c>active/</c> (job card 395-B: an
/// unstaged edit left the move commit a pure rename, so the developer got an empty Result).
/// A pure rename keeps the blob id, so the check compares blob ids: <see cref="Observe"/> notes
/// the card's blob each tick while it is in <c>active/</c>; <see cref="Check"/> compares that with
/// the card's blob in <c>done/</c> or <c>review/</c> on main when the run ends. 1063-B: once the repository's
/// model of main has loaded (<see cref="Feed"/>), the blob ids are its snapshot's and the fetch is its gate's (MM-9, MM-14).
/// </summary>
public sealed class FinishedCardCheck(IMainRepository main, string jobsDir)
{
    /// <summary>The end reason recorded and shown for a flagged run.</summary>
    public const string Note = "finished without updating the card";

    private readonly Dictionary<string, string> _lastActive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FinishedCardFlag> _flagged = new(StringComparer.Ordinal);

    /// <summary>1063-B: the repository's model of main; while it has no snapshot main is read through git.</summary>
    public IMainFeed? Feed { get; set; }

    /// <summary>
    /// Cards flagged since the program started, each with the folder and the blob main showed when it
    /// was flagged (410-A). The page's Waiting on you row leaves once either changes, which is what
    /// makes the developer's Done (MOVE-6) clear the row; a flag with only a name would never leave.
    /// </summary>
    public IReadOnlyCollection<FinishedCardFlag> Flagged
    {
        get
        {
            lock (_flagged)
            {
                return [.. _flagged.Values];
            }
        }
    }

    /// <summary>Note the blob id of <paramref name="card"/> (a card name, no extension) in active/ on main, if it is there.</summary>
    public void Observe(string card)
    {
        var blob = BlobIn("active", card);
        if (blob is not null)
        {
            _lastActive[card] = blob;
        }
    }

    /// <summary>
    /// After a run of <paramref name="card"/> ended as <paramref name="result"/>: <see cref="Note"/>
    /// when the result is done or review and the card now in done/ or review/ has the blob last seen
    /// in active/, otherwise null. The flag keeps that folder and blob, which is what lets the page's
    /// row leave when the developer's Done moves the card on (410-A). Forgets the observation either way.
    /// </summary>
    public string? Check(string card, string result)
    {
        var seen = _lastActive.Remove(card, out var before) ? before : null;
        if (seen is null || (result != "done" && result != "review"))
        {
            return null;
        }

        if (Feed is { Model: not null } feed)
        {
            if (feed.Fetch(FetchAsk.Poll) != FetchAnswer.Fetched)
            {
                _ = feed.Tick();   // MM-8: a fetch that ran has read the ref already
            }
        }
        else
        {
            _ = main.Fetch();
        }
        var folder = CardFolder.Review;
        var now = BlobIn("review", card);
        if (now is null)
        {
            folder = CardFolder.Done;
            now = BlobIn("done", card);
        }

        if (now is null || !string.Equals(now, seen, StringComparison.Ordinal))
        {
            return null;
        }

        lock (_flagged)
        {
            _flagged[card] = new FinishedCardFlag { Card = card, Folder = folder, Stamp = now };
        }

        return Note;
    }

    private string? BlobIn(string folder, string card)
    {
        var path = $"{jobsDir}/{folder}/{card}.md";
        if (Feed?.Model?.Snapshot is { } snapshot)
        {
            return snapshot.Files.GetValueOrDefault(path);
        }

        return main.ListTree($"{jobsDir}/{folder}")?.FirstOrDefault(e => e.Path == path)?.BlobId;
    }
}
