// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Host.Locks;

namespace Scheduler.Host.Tests.Locks;

/// <summary>1189-B, DL-6: the tracked-file list reaches the rules, is cached per main sha, and keeps the last list when git fails.</summary>
public sealed class TrackedFilesTests
{
    private sealed class Main : IMainRepository
    {
        public string? Sha = "a";
        public int Lists;
        public IReadOnlyList<TreeEntry>? Tree = [];

        public bool Fetch() => true;

        public IReadOnlyList<TreeEntry>? ListTree(string folder)
        {
            Lists++;
            return Tree;
        }

        public string? ReadBlob(string blobId) => null;

        public string? ReadFile(string path) => null;

        public string? HeadSha() => Sha;
    }

    private static IReadOnlyList<TreeEntry> Files(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new TreeEntry { Path = $"big/f{i}.txt", BlobId = "b" })];

    [Fact]
    public void MoreThanTwoHundredFilesUnderOneEntryIsTooBroad()
    {
        var main = new Main { Tree = Files(201) };
        var rules = new LockRules { TrackedFiles = new TrackedFiles(main).Current() };
        Assert.Equal(201, rules.TrackedFiles!.Count);
        Assert.True(LockArbiter.IsTooBroad("big/", rules));
        Assert.False(LockArbiter.IsTooBroad("big/f1.txt", rules));
    }

    [Fact]
    public void ListedOncePerMainShaAndKeptWhenGitFails()
    {
        var main = new Main { Tree = Files(3) };
        var tracked = new TrackedFiles(main);
        Assert.Equal(3, tracked.Current()!.Count);
        Assert.Equal(3, tracked.Current()!.Count);
        Assert.Equal(1, main.Lists);

        main.Sha = "b";
        main.Tree = Files(4);
        Assert.Equal(4, tracked.Current()!.Count);
        Assert.Equal(2, main.Lists);

        main.Sha = null;
        Assert.Equal(4, tracked.Current()!.Count);
    }
}
