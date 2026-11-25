// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>987-B: the card read costs one rev-parse when main has not moved.</summary>
public class CardReaderByCommitTests
{
    private const string Jobs = "jobs";

    [Fact]
    public void Read_MainUnchanged_ListsNoTreeAndOpensNoBlob()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/queue/010-A-x.md", "b1", "# x\n\nStatus: ready\n");
        repo.Set("jobs/queue/020-A-y.md", "b2", "# y\n\nStatus: ready\n");
        var reader = new CardReader(repo, Jobs, new ListLog()) { ByCommit = true };

        Assert.Equal(2, reader.Read().Count);
        Assert.Equal(2, reader.Read().Count);

        Assert.Equal(1, repo.Trees);
        Assert.Equal(2, repo.Blobs);
    }

    [Fact]
    public void Read_MainMoved_OpensOnlyTheChangedBlob()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/queue/010-A-x.md", "b1", "# x\n\nStatus: ready\n");
        repo.Set("jobs/queue/020-A-y.md", "b2", "# y\n\nStatus: ready\n");
        var reader = new CardReader(repo, Jobs, new ListLog()) { ByCommit = true };
        reader.Read();

        repo.Set("jobs/queue/020-A-y.md", "b3", "# y\n\nStatus: blocked\n");
        var cards = reader.Read();

        Assert.Equal(2, repo.Trees);
        Assert.Equal(3, repo.Blobs);
        Assert.Equal("blocked", cards.Single(c => c.Id == "020-A").Status);
    }

    // A blob that could not be read leaves the read incomplete: the next one lists the tree again.
    [Fact]
    public void Read_UnreadableBlob_IsTriedAgainAtTheSameCommit()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/queue/010-A-x.md", "b1", null);
        var reader = new CardReader(repo, Jobs, new ListLog()) { ByCommit = true };
        Assert.Empty(reader.Read());

        repo.Fix("b1", "# x\n\nStatus: ready\n");
        Assert.Single(reader.Read());
        Assert.Equal(2, repo.Trees);
    }

    // Off (the default, and every fake whose sha never moves), each read lists the tree as before.
    [Fact]
    public void Read_ByCommitOff_ListsTheTreeEachTime()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/queue/010-A-x.md", "b1", "# x\n\nStatus: ready\n");
        var reader = new CardReader(repo, Jobs, new ListLog());
        reader.Read();
        reader.Read();
        Assert.Equal(2, repo.Trees);
    }

    private sealed class CountingRepo : IMainRepository
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _blobs = new(StringComparer.Ordinal);
        private int _commit;

        public int Trees { get; private set; }

        public int Blobs { get; private set; }

        public void Set(string path, string blob, string? text)
        {
            _paths[path] = blob;
            _blobs[blob] = text;
            _commit++;
        }

        public void Fix(string blob, string text) => _blobs[blob] = text;

        public bool Fetch() => true;

        public IReadOnlyList<TreeEntry>? ListTree(string folder)
        {
            Trees++;
            return _paths.Select(p => new TreeEntry { Path = p.Key, BlobId = p.Value }).ToList();
        }

        public string? ReadBlob(string blobId)
        {
            Blobs++;
            return _blobs.GetValueOrDefault(blobId);
        }

        public string? ReadFile(string path) => null;

        public string? HeadSha() => "c" + _commit;
    }
}
