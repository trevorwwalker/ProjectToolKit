// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Locks;

/// <summary>
/// DL-6: the repository's tracked files at main, for the "more than 200 files under one entry" check.
/// One <c>git ls-tree -r</c> of the whole tree, cached per main sha so a lock step costs a rev-parse only.
/// </summary>
public sealed class TrackedFiles
{
    private readonly IMainRepository _main;
    private string? _sha;
    private IReadOnlyList<string>? _files;

    public TrackedFiles(IMainRepository main) => _main = main;

    /// <summary>The tracked paths at main's head, or null when git cannot say (the count is then not checked).</summary>
    public IReadOnlyList<string>? Current()
    {
        var sha = _main.HeadSha();
        if (sha is null)
        {
            return _files;
        }

        if (sha == _sha && _files is not null)
        {
            return _files;
        }

        var tree = _main.ListTree(".");
        if (tree is null)
        {
            return _files;
        }

        _sha = sha;
        _files = [.. tree.Select(e => e.Path)];
        return _files;
    }
}
