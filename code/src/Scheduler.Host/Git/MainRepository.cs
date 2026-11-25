// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Git;

/// <summary>
/// <see cref="IMainRepository"/> over git: reads only <c>&lt;remote&gt;/&lt;branch&gt;</c> of the
/// repository at <c>repoDir</c>, never its working tree (CARD-1), with git never prompting (CFG-16).
/// </summary>
public sealed class MainRepository : IMainRepository, IBlobBatchReader
{
    private readonly GitRunner _git;
    private readonly string _repoDir;
    private readonly string _remote;
    private readonly string _branch;

    public MainRepository(GitRunner git, string repoDir, string remote, string branch)
    {
        _git = git;
        _repoDir = repoDir;
        _remote = remote;
        _branch = branch;
    }

    private string Ref => _remote + "/" + _branch;

    /// <inheritdoc />
    public bool Fetch() => _git.Run(_repoDir, "fetch", "-q", _remote).Ok;

    /// <inheritdoc />
    public IReadOnlyList<TreeEntry>? ListTree(string folder)
    {
        var r = _git.Run(_repoDir, "-c", "core.quotepath=off", "ls-tree", "-r", Ref, "--", folder.TrimEnd('/') + "/");
        if (!r.Ok)
        {
            return null;
        }

        var entries = new List<TreeEntry>();
        foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> blob <id>\t<path>"
            var tab = line.IndexOf('\t');
            var head = tab < 0 ? [] : line[..tab].Split(' ');
            if (head.Length == 3 && head[1] == "blob")
            {
                entries.Add(new TreeEntry { Path = line[(tab + 1)..].TrimEnd('\r'), BlobId = head[2] });
            }
        }

        return entries.Count == 0 ? null : entries;
    }

    /// <inheritdoc />
    public string? ReadBlob(string blobId)
    {
        var r = _git.Run(_repoDir, "cat-file", "blob", blobId);
        return r.Ok ? r.Stdout : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ReadBlobs(IReadOnlyCollection<string> blobIds)
    {
        if (blobIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var output = _git.RunBinary(_repoDir, BlobBatch.Input(blobIds), "cat-file", "--batch");
        return output is null ? new Dictionary<string, string>(StringComparer.Ordinal) : BlobBatch.Parse(output);
    }

    /// <inheritdoc />
    public string? ReadFile(string path)
    {
        var r = _git.Run(_repoDir, "show", Ref + ":" + path);
        return r.Ok ? r.Stdout : null;
    }

    /// <inheritdoc />
    public string? HeadSha()
    {
        var r = _git.Run(_repoDir, "rev-parse", Ref);
        return r.Ok ? r.Stdout.Trim() : null;
    }
}
