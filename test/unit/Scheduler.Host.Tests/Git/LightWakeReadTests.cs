// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>1002-B: a light wake reads no request it has read before and pays no fetch it does not need.</summary>
public class LightWakeReadTests
{
    private const string Jobs = "jobs";

    [Fact]
    public void Requests_MainUnchanged_AreNotReadAgain()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/requests/001-a.md", "b1", "# A\n\nStatus: open\n");
        repo.Set("jobs/requests/002-b.md", "b2", "# B\n\nStatus: open\n");
        var reader = new RequestReader(repo, Jobs);

        Assert.Equal(2, reader.Read().Count);
        Assert.Equal(["A", "B"], reader.Read().Select(r => r.Title));

        Assert.Equal(1, repo.Trees);
        Assert.Equal(2, repo.Blobs);
        Assert.Equal(0, repo.Files);
    }

    [Fact]
    public void Requests_MainMoved_OpensOnlyTheChangedBlob()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/requests/001-a.md", "b1", "# A\n\nStatus: open\n");
        repo.Set("jobs/requests/002-b.md", "b2", "# B\n\nStatus: open\n");
        var reader = new RequestReader(repo, Jobs);
        reader.Read();

        repo.Set("jobs/requests/002-b.md", "b3", "# B\n\nStatus: answered\n");
        var requests = reader.Read();

        Assert.Equal("answered", requests.Single(r => r.Title == "B").Status);
        Assert.Equal(2, repo.Trees);
        Assert.Equal(3, repo.Blobs);
        Assert.Equal(["jobs/requests"], repo.Folders.Distinct());
    }

    [Fact]
    public void Requests_UnreadableBlob_IsTriedAgainAtTheSameCommit()
    {
        var repo = new CountingRepo();
        repo.Set("jobs/requests/001-a.md", "b1", null);
        var reader = new RequestReader(repo, Jobs);
        Assert.Equal("", reader.Read().Single().Title);

        repo.Fix("b1", "# A\n");
        Assert.Equal("A", reader.Read().Single().Title);
    }

    [Theory]
    [InlineData(TickWake.Click)]
    [InlineData(TickWake.MoveRequest)]
    public void Fetch_ClickOrMoveRequest_DoesNotFetch(TickWake wake)
    {
        var (repo, _, fetcher) = Fetcher();
        fetcher.Woke(TickWake.Poll);
        Assert.True(fetcher.Fetch());

        fetcher.Woke(wake);
        Assert.True(fetcher.Fetch());

        Assert.Equal(1, repo.Fetches);
    }

    [Theory]
    [InlineData(TickWake.Poll)]
    [InlineData(TickWake.WorkerExit)]
    [InlineData(TickWake.PlannerBatch)]
    [InlineData(TickWake.HealthCheck)]
    public void Fetch_PollAndOtherWakes_Fetch(TickWake wake)
    {
        var (repo, _, fetcher) = Fetcher();
        fetcher.Woke(TickWake.Poll);
        fetcher.Fetch();

        fetcher.Woke(wake);
        Assert.True(fetcher.Fetch());

        Assert.Equal(2, repo.Fetches);
    }

    [Fact]
    public void Fetch_WorkerFinished_FetchesEvenOnAClick()
    {
        var (repo, _, fetcher) = Fetcher();
        fetcher.Fetch();

        fetcher.Ask();
        fetcher.Woke(TickWake.Click);
        fetcher.Fetch();

        Assert.Equal(2, repo.Fetches);
    }

    [Fact]
    public void Fetch_Failed_IsMadeAgainOnTheNextClick()
    {
        var (repo, _, fetcher) = Fetcher();
        repo.FetchOk = false;
        Assert.False(fetcher.Fetch());

        repo.FetchOk = true;
        fetcher.Woke(TickWake.Click);
        Assert.True(fetcher.Fetch());

        Assert.Equal(2, repo.Fetches);
    }

    [Fact]
    public void Fetch_WakeNotKnown_FollowsThePollInterval()
    {
        var (repo, clock, fetcher) = Fetcher();
        fetcher.Fetch();

        clock.Skew = TimeSpan.FromSeconds(5);
        fetcher.Fetch();
        Assert.Equal(1, repo.Fetches);

        clock.Skew = TimeSpan.FromSeconds(14);
        fetcher.Fetch();
        Assert.Equal(2, repo.Fetches);
    }

    private static (CountingRepo Repo, FixedClock Clock, MainFetcher Fetcher) Fetcher()
    {
        var repo = new CountingRepo();
        var clock = new FixedClock();
        return (repo, clock, new MainFetcher(repo, clock, TimeSpan.FromSeconds(13)));
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _start = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        public TimeSpan Skew { get; set; }

        public DateTimeOffset Now => _start + Skew;
    }

    private sealed class CountingRepo : IMainRepository
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _blobs = new(StringComparer.Ordinal);
        private int _commit;

        public int Trees { get; private set; }

        public int Blobs { get; private set; }

        public int Files { get; private set; }

        public int Fetches { get; private set; }

        public bool FetchOk { get; set; } = true;

        public List<string> Folders { get; } = [];

        public void Set(string path, string blob, string? text)
        {
            _paths[path] = blob;
            _blobs[blob] = text;
            _commit++;
        }

        public void Fix(string blob, string text) => _blobs[blob] = text;

        public bool Fetch()
        {
            Fetches++;
            return FetchOk;
        }

        public IReadOnlyList<TreeEntry>? ListTree(string folder)
        {
            Trees++;
            Folders.Add(folder);
            return _paths.Select(p => new TreeEntry { Path = p.Key, BlobId = p.Value }).ToList();
        }

        public string? ReadBlob(string blobId)
        {
            Blobs++;
            return _blobs.GetValueOrDefault(blobId);
        }

        public string? ReadFile(string path)
        {
            Files++;
            return null;
        }

        public string? HeadSha() => "c" + _commit;
    }
}
