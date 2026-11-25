// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Git;

/// <summary>
/// 1002-B: the requests in <c>{jobsDir}/requests/</c> on main. When main has not moved since the
/// last whole read nothing is read; otherwise the folder is listed and only blobs not read before
/// are opened: 1013-B, all of them in one <c>cat-file --batch</c> when the repository can
/// (<see cref="IBlobBatchReader"/>), else one <c>cat-file</c> each. Only the requests directly in the
/// folder are opened (<see cref="RequestFiles.Read"/>), never <c>answered/</c>. 1063-B: once the repository's
/// model of main has loaded (<see cref="Feed"/>), the requests are its snapshot's, with no git process (MM-14).
/// </summary>
public sealed class RequestReader
{
    private readonly IMainRepository _repo;
    private readonly string _jobsDir;
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);   // blob id -> text
    private IReadOnlyList<JobRequest> _requests = [];
    private string? _readSha;

    /// <summary>A reader of the requests under <paramref name="jobsDir"/> on <paramref name="repo"/>'s main.</summary>
    public RequestReader(IMainRepository repo, string jobsDir)
    {
        _repo = repo;
        _jobsDir = jobsDir.TrimEnd('/');
    }

    /// <summary>1063-B: the repository's model of main; while it has no snapshot the requests are read through git.</summary>
    public IMainFeed? Feed { get; set; }

    /// <summary>The requests now; an empty list when the folder cannot be listed.</summary>
    public IReadOnlyList<JobRequest> Read()
    {
        if (Feed?.Model?.Snapshot is { } snapshot)
        {
            _readSha = null;
            _texts.Clear();
            return snapshot.Requests(_jobsDir);
        }

        var sha = _repo.HeadSha();
        if (sha is not null && string.Equals(sha, _readSha, StringComparison.Ordinal))
        {
            return _requests;
        }

        var tree = _repo.ListTree(_jobsDir + "/requests");
        if (tree is null)
        {
            _readSha = null;
            _requests = [];
            return _requests;
        }

        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in tree)
        {
            blobs[e.Path] = e.BlobId;
        }

        Prefetch(blobs);

        var whole = true;
        var used = new HashSet<string>(StringComparer.Ordinal);
        _requests = RequestFiles.Read(_jobsDir, blobs.Keys, path =>
        {
            var blob = blobs[path];
            used.Add(blob);
            if (_texts.TryGetValue(blob, out var cached))
            {
                return cached;
            }

            var text = _repo.ReadBlob(blob);
            if (text is null)
            {
                whole = false;   // read again next time
                return null;
            }

            _texts[blob] = text;
            return text;
        });

        foreach (var gone in _texts.Keys.Where(k => !used.Contains(k)).ToList())
        {
            _texts.Remove(gone);
        }

        _readSha = whole ? sha : null;
        return _requests;
    }

    // 1013-B: the blobs the read below would open, read in one git process; any it could not read are
    // opened one by one as before.
    private void Prefetch(Dictionary<string, string> blobs)
    {
        if (_repo is not IBlobBatchReader batch)
        {
            return;
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        _ = RequestFiles.Read(_jobsDir, blobs.Keys, path =>
        {
            if (!_texts.ContainsKey(blobs[path]))
            {
                wanted.Add(blobs[path]);
            }

            return null;
        });
        if (wanted.Count < 2)
        {
            return;
        }

        foreach (var (id, text) in batch.ReadBlobs(wanted))
        {
            _texts[id] = text;
        }
    }
}
