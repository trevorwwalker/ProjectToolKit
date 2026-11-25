// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Host.Git;

/// <summary>
/// Reads the job cards from main (A2) through <see cref="IMainRepository"/>: CARD-1 (a blob is
/// parsed again only when its id or path changes), CARD-3 (the previous list stays when git
/// fails), CARD-5 (done/ cards are never opened) and CARD-19 (stale queue/ copies). 1063-B: once the
/// repository's model of main has loaded (<see cref="Feed"/>), the cards come from its snapshot, one commit
/// throughout, with no git process (MM-14, MM-15); the list is the same as the CLI read's (MM-18).
/// </summary>
public sealed class CardReader
{
    private readonly IMainRepository _repo;
    private readonly string _jobsDir;
    private readonly ISchedulerLog _log;
    private readonly Func<Card, string, Card> _parse;
    private readonly Dictionary<string, Card> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedDuplicates = new(StringComparer.Ordinal);
    private IReadOnlyList<Card> _cards = [];
    private string? _readSha;
    private string? _modelSha;

    /// <summary>
    /// 987-B: when true, a read at the main commit the last whole read was at hands back those cards
    /// without listing the tree again (one rev-parse instead of an ls-tree of the jobs folder); a
    /// moved main lists it, and only changed blobs are opened (the blob cache, CARD-1).
    /// </summary>
    public bool ByCommit { get; init; }

    /// <summary>
    /// Dynamic locks (DL-19, 1021-B): called with main's commit on every read that knows it (<see cref="ByCommit"/>),
    /// before the cards are read; the audit of main runs here. What it throws is logged, never passed on.
    /// </summary>
    public Action<string>? MainSeen { get; set; }

    /// <summary>1063-B: the repository's model of main; while it has no snapshot the cards are read through git.</summary>
    public IMainFeed? Feed { get; set; }

    /// <param name="parse">Parses a card's text; <see cref="CardFiles.ParseText"/> unless a test counts the calls.</param>
    public CardReader(IMainRepository repo, string jobsDir, ISchedulerLog log, Func<Card, string, Card>? parse = null)
    {
        _repo = repo;
        _jobsDir = jobsDir;
        _log = log;
        _parse = parse ?? CardFiles.ParseText;
    }

    /// <summary>The cards on the remote branch now, or the previous list when git failed (CARD-3).</summary>
    public IReadOnlyList<Card> Read()
    {
        if (Feed?.Model?.Snapshot is { } snapshot)
        {
            return FromModel(snapshot);
        }

        _modelSha = null;
        // 987-B: main has not moved since the last whole read, so nothing in the jobs folder has.
        var sha = ByCommit ? _repo.HeadSha() : null;
        if (sha is not null)
        {
            Seen(sha);
        }

        if (sha is not null && string.Equals(sha, _readSha, StringComparison.Ordinal))
        {
            return _cards;
        }

        var tree = _repo.ListTree(_jobsDir);
        if (tree is null)
        {
            return _cards;
        }

        var fetched = Prefetch(tree);
        var whole = true;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<Card>();
        foreach (var e in tree)
        {
            var key = e.BlobId + "|" + e.Path;
            seen.Add(key);
            if (_cache.TryGetValue(key, out var cached))
            {
                cards.Add(cached);
                continue;
            }

            var card = CardFiles.FromPath(_jobsDir, e.Path);
            if (card is null)
            {
                continue;
            }

            // The blob comes from the tree, not the text (CARD-1, CARD-5): it is the stamp
            // PageWaiting compares when it decides a flagged card has moved on (410-A).
            card = card with { BlobId = e.BlobId };

            if (card.Folder != CardFolder.Done)
            {
                var text = fetched.TryGetValue(e.BlobId, out var got) ? got : _repo.ReadBlob(e.BlobId);
                if (text is null)
                {
                    seen.Remove(key);
                    whole = false;
                    continue; // unreadable now: left out and tried again next time
                }

                card = _parse(card, text);
            }

            _cache[key] = card;
            cards.Add(card);
        }

        foreach (var k in _cache.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _cache.Remove(k);
        }

        _cards = cards;
        _readSha = whole ? sha : null;   // a card left out is read again next time
        return _cards;
    }

    // 1063-B: the jobs folder's files at the snapshot's commit in tree order, as the CLI read lists them: an open
    // card as the model parsed it (CARD-1: parsed once per blob), a done/ card from its path only (CARD-5).
    private IReadOnlyList<Card> FromModel(MainSnapshot snapshot)
    {
        Seen(snapshot.Commit);
        _readSha = null;
        if (string.Equals(snapshot.Commit, _modelSha, StringComparison.Ordinal))
        {
            return _cards;
        }

        var open = new Dictionary<string, Card>(StringComparer.Ordinal);
        foreach (var c in snapshot.Cards(_jobsDir))
        {
            open[c.Path] = c;
        }

        var cards = new List<Card>();
        foreach (var e in snapshot.Entries(_jobsDir))
        {
            if (CardFiles.FromPath(_jobsDir, e.Path) is not { } card)
            {
                continue;
            }

            if (card.Folder == CardFolder.Done)
            {
                cards.Add(card with { BlobId = e.BlobId });
            }
            else if (open.TryGetValue(e.Path, out var parsed))
            {
                cards.Add(parsed);
            }
        }

        _cache.Clear();
        _cards = cards;
        _modelSha = snapshot.Commit;
        return _cards;
    }

    private void Seen(string sha)
    {
        if (MainSeen is not { } onMain)
        {
            return;
        }

        try
        {
            onMain(sha);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Write($"locks: audit of main failed: {e.Message}");
        }
    }

    // 1013-B: the blobs the read would open (not cached, not done/), read in one git process when the
    // repository can; any it could not read are opened one by one as before.
    private IReadOnlyDictionary<string, string> Prefetch(IReadOnlyList<TreeEntry> tree)
    {
        if (_repo is not IBlobBatchReader batch)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in tree)
        {
            if (!_cache.ContainsKey(e.BlobId + "|" + e.Path)
                && CardFiles.FromPath(_jobsDir, e.Path) is { } card && card.Folder != CardFolder.Done)
            {
                wanted.Add(e.BlobId);
            }
        }

        return wanted.Count < 2 ? new Dictionary<string, string>(StringComparer.Ordinal) : batch.ReadBlobs(wanted);
    }

    /// <summary>
    /// One read with CARD-19 applied: a queue/ card whose Id is also in another folder is stale,
    /// removed from main (up to 3 tries) and the cards read again; copies outside queue/ are
    /// logged once and left alone. A stale copy that could not be removed is never returned.
    /// </summary>
    public IReadOnlyList<Card> ReadFixingDuplicates(IJobsCommitter committer)
    {
        var cards = Read();
        var removed = false;
        foreach (var group in cards.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var stale = group.Where(c => c.Folder == CardFolder.Queue).ToList();
            if (stale.Count == 0 || stale.Count == group.Count())
            {
                if (stale.Count == 0 && _loggedDuplicates.Add(group.Key))
                {
                    _log.Write($"duplicate card {group.Key} in {string.Join(" and ", group.Select(c => c.Folder.ToString().ToLowerInvariant()))}: left alone");
                }

                continue;
            }

            foreach (var c in stale)
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var result = committer.Apply(new JobsChange
                    {
                        Removals = [c.Path],
                        Message = $"jobs: remove the queue/ copy of {c.Name}",
                    });
                    if (result.Ok)
                    {
                        removed = true;
                        break;
                    }
                }
            }
        }

        if (removed)
        {
            _repo.Fetch();
            _ = Feed?.Tick();   // 1063-B: the model sees the removal through the ref (MM-8)
            cards = Read();
        }

        var staleIds = cards.GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Any(c => c.Folder != CardFolder.Queue))
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return cards.Where(c => !(c.Folder == CardFolder.Queue && staleIds.Contains(c.Id))).ToList();
    }
}
