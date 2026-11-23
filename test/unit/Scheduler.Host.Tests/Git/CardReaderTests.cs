using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

[ParallelMethods]
public class CardReaderTests
{
    private const string Jobs = "docs/jobs";

    private static string Body(string status) => $"# Card\n\nStatus: {status}\n";

    // CARD-1: only <remote>/<branch> is read: not an uncommitted edit, not an untracked file.
    [Fact]
    public void Cards_ReadFromRemoteMainOnly_NotWorkingTree()
    {
        using var fx = new GitFixture();
        fx.Push("one", new Dictionary<string, string?> { [$"{Jobs}/queue/010-A-x.md"] = Body("ready") });
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        File.WriteAllText(Path.Combine(fx.Repo, Jobs, "queue", "010-A-x.md"), Body("blocked"));
        File.WriteAllText(Path.Combine(fx.Repo, Jobs, "queue", "020-A-y.md"), Body("ready"));

        var main = fx.Main();
        Assert.True(main.Fetch());
        var cards = new CardReader(main, Jobs, new ListLog()).Read();

        var card = Assert.Single(cards);
        Assert.Equal("010-A", card.Id);
        Assert.Equal("ready", card.Status);
    }

    // CARD-1 (410-A): each card carries the blob id the tree gave it, which is what tells a card
    // rewritten since 395-B flagged it from one that never changed. A done/ card gets one too,
    // although CARD-5 keeps it unopened.
    [Fact]
    public void Cards_BlobId_ComesFromTheTree()
    {
        using var fx = new GitFixture();
        fx.Push("blobs", new Dictionary<string, string?>
        {
            [$"{Jobs}/review/010-A-x.md"] = Body("review"),
            [$"{Jobs}/done/020-A-y.md"] = Body("done"),
        });

        var main = fx.Main();
        Assert.True(main.Fetch());
        var cards = new CardReader(main, Jobs, new ListLog()).Read();

        Assert.Equal(2, cards.Count);
        foreach (var card in cards)
        {
            Assert.Equal(GitFixture.Git(fx.Seed, "rev-parse", $"HEAD:{card.Path}"), card.BlobId);
        }
    }

    // CARD-1: a blob is parsed again only when its id changes; a moved card (same blob, new path) is parsed again.
    [Fact]
    public void Cards_BlobCache_ReparsesOnlyChangedBlobs()
    {
        using var fx = new GitFixture();
        fx.Push("two", new Dictionary<string, string?>
        {
            [$"{Jobs}/queue/010-A-x.md"] = Body("ready"),
            [$"{Jobs}/queue/020-A-y.md"] = Body("ready"),
        });
        var main = fx.Main();
        var parsed = new List<string>();
        var reader = new CardReader(main, Jobs, new ListLog(), (c, t) =>
        {
            parsed.Add(c.Path);
            return CardFiles.ParseText(c, t);
        });

        main.Fetch();
        Assert.Equal(2, reader.Read().Count);
        Assert.Equal(2, parsed.Count);

        parsed.Clear();
        fx.Push("change one", new Dictionary<string, string?> { [$"{Jobs}/queue/010-A-x.md"] = Body("blocked") });
        main.Fetch();
        Assert.Equal(2, reader.Read().Count);
        Assert.Equal([$"{Jobs}/queue/010-A-x.md"], parsed);

        parsed.Clear();
        Directory.CreateDirectory(Path.Combine(fx.Seed, Jobs, "active"));
        GitFixture.Git(fx.Seed, "mv",$"{Jobs}/queue/020-A-y.md", $"{Jobs}/active/020-A-y.md");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "move");
        GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:main");
        main.Fetch();
        Assert.Equal(2, reader.Read().Count);
        Assert.Equal([$"{Jobs}/active/020-A-y.md"], parsed);

        parsed.Clear();
        reader.Read();
        Assert.Empty(parsed);
    }

    // CARD-3: when ls-tree gives nothing (the remote ref is gone), the previous list is kept.
    [Fact]
    public void Cards_LsTreeFails_KeepsPreviousList()
    {
        using var fx = new GitFixture();
        fx.Push("three", new Dictionary<string, string?>
        {
            [$"{Jobs}/queue/010-A-x.md"] = Body("ready"),
            [$"{Jobs}/queue/020-A-y.md"] = Body("ready"),
            [$"{Jobs}/active/030-A-z.md"] = Body("ready"),
        });
        var main = fx.Main();
        main.Fetch();
        var reader = new CardReader(main, Jobs, new ListLog());
        Assert.Equal(3, reader.Read().Count);

        GitFixture.Git(fx.Repo, "update-ref", "-d", "refs/remotes/origin/main");

        Assert.Equal(3, reader.Read().Count);
    }

    // CARD-19: a queue/ copy of a card that is also in another folder is never a candidate and is removed from main.
    [Fact]
    public void Cards_DuplicateInQueue_RemovedFromMain()
    {
        using var fx = new GitFixture();
        fx.Push("dup", new Dictionary<string, string?>
        {
            [$"{Jobs}/queue/010-A-x.md"] = Body("ready"),
            [$"{Jobs}/active/010-A-x.md"] = Body("ready"),
        });
        var main = fx.Main();
        main.Fetch();
        var committer = new SeedCommitter(fx);
        var reader = new CardReader(main, Jobs, new ListLog());

        var cards = reader.ReadFixingDuplicates(committer);

        var card = Assert.Single(cards);
        Assert.Equal(CardFolder.Active, card.Folder);
        Assert.Equal(["jobs: remove the queue/ copy of 010-A-x"], committer.Messages);
        Assert.DoesNotContain("queue/010-A-x.md", GitFixture.Git(fx.Bare, "ls-tree", "-r", "--name-only", "main"));
    }

    // CARD-19: a failing removal is tried up to 3 times; the stale copy is still never returned.
    [Fact]
    public void Cards_DuplicateInQueue_RemovalRetriesThreeTimes()
    {
        using var fx = new GitFixture();
        fx.Push("dup", new Dictionary<string, string?>
        {
            [$"{Jobs}/queue/010-A-x.md"] = Body("ready"),
            [$"{Jobs}/review/010-A-x.md"] = Body("ready"),
        });
        var main = fx.Main();
        main.Fetch();
        var failing = new FailingCommitter();

        var cards = new CardReader(main, Jobs, new ListLog()).ReadFixingDuplicates(failing);

        Assert.Equal(3, failing.Calls);
        Assert.Equal(CardFolder.Review, Assert.Single(cards).Folder);
    }

    // CARD-19: copies outside queue/ are logged once over three ticks and left alone.
    [Fact]
    public void Cards_DuplicateOutsideQueue_LoggedOnceLeftAlone()
    {
        using var fx = new GitFixture();
        fx.Push("dup", new Dictionary<string, string?>
        {
            [$"{Jobs}/active/010-A-x.md"] = Body("ready"),
            [$"{Jobs}/review/010-A-x.md"] = Body("ready"),
        });
        var main = fx.Main();
        main.Fetch();
        var log = new ListLog();
        var committer = new SeedCommitter(fx);
        var reader = new CardReader(main, Jobs, log);

        for (var i = 0; i < 3; i++)
        {
            reader.ReadFixingDuplicates(committer);
        }

        Assert.Single(log.Lines);
        Assert.Empty(committer.Messages);
    }

    private sealed class FailingCommitter : IJobsCommitter
    {
        public int Calls { get; private set; }

        public CommitResult Apply(JobsChange change)
        {
            Calls++;
            return new CommitResult { Ok = false, Error = "rejected" };
        }
    }
}
