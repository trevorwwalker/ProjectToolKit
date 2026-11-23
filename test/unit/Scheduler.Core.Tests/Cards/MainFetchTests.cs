using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// The fetch that precedes reading the cards (CARD-2, <c>Fetch-Main</c> L532-536): at most one
/// <c>git fetch</c> every 15 seconds, so a tick that runs faster than that does not spawn git
/// processes. A fake clock and a fake git stand in for both sides.
/// Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class MainFetchTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class FakeGit(bool works = true) : IMainRepository
    {
        public int Fetches { get; private set; }

        public bool Fetch()
        {
            Fetches++;
            return works;
        }

        public IReadOnlyList<TreeEntry>? ListTree(string folder) => [];

        public string? ReadBlob(string blobId) => null;

        public string? ReadFile(string path) => null;

        public string? HeadSha() => null;
    }

    // CARD-2. Boundary: 15 s exactly is due (the script's test is -lt 15).
    [Fact]
    public void Cards_Fetch_AtMostEvery15Seconds()
    {
        var clock = new FakeClock();
        var git = new FakeGit();
        var fetch = new MainFetch(git, clock);

        Assert.True(fetch.Fetch());                        // t = 0 s: the first one
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(fetch.Fetch());                       // t = 10 s: too soon, no git process
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(fetch.Fetch());                        // t = 15 s: exactly the boundary
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.False(fetch.Fetch());                       // t = 29 s
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(fetch.Fetch());                        // t = 30 s

        Assert.Equal(3, git.Fetches);
    }

    // CARD-2 (L534): the script throws the fetch's result away and re-stamps the time anyway, so a
    // failing fetch is not retried until the next 15 s has passed.
    [Fact]
    public void Cards_Fetch_KeepsTheCadenceWhenGitFails()
    {
        var clock = new FakeClock();
        var git = new FakeGit(works: false);
        var fetch = new MainFetch(git, clock);

        Assert.True(fetch.Fetch());
        Assert.False(fetch.Fetch());
        Assert.Equal(1, git.Fetches);

        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.True(fetch.Fetch());
        Assert.Equal(2, git.Fetches);
    }
}
