// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Pool;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// Card 1053-B: the sweep deletes the wip branches of done cards whose work is on main (one push), keeps and logs
/// the others once a day, leaves cards not in done/ alone, and runs on a task of its own, once per day.
/// </summary>
[ParallelMethods]
public class WipSweepTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 5, 0, 0, TimeSpan.Zero);

    private static GitFixture Setup()
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { ["f.txt"] = "1\n" });
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        return fx;
    }

    private static void PushBranch(GitFixture fx, string branch, IDictionary<string, string> files)
    {
        GitFixture.Git(fx.Seed, "checkout", "-q", "-b", "tmp-" + branch.Replace('/', '-'));
        foreach (var (path, text) in files)
        {
            File.WriteAllText(Path.Combine(fx.Seed, path), text);
        }

        GitFixture.Git(fx.Seed, "add", "-A");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "wip");
        GitFixture.Git(fx.Seed, "push", "-q", "origin", $"HEAD:{branch}");
        GitFixture.Git(fx.Seed, "checkout", "-q", "main");
    }

    private static bool Exists(GitFixture fx, string branch) => GitFixture.Git(fx.Bare, "branch", "--list", branch).Length > 0;

    private static SweepRepo Repo(GitFixture fx, ListLog log, params string[] done) =>
        new("p", fx.Repo, "origin", "main", () => done, log);

    [Fact]
    public void MergedBranchOfDoneCard_IsDeleted_UnmergedKept_NotDoneUntouched()
    {
        using var fx = Setup();
        PushBranch(fx, "wip/1-A", new Dictionary<string, string> { ["f.txt"] = "2\n" });
        PushBranch(fx, "wip/2-A", new Dictionary<string, string> { ["new.txt"] = "x\n" });
        PushBranch(fx, "wip/3-A", new Dictionary<string, string> { ["f.txt"] = "2\n" });
        fx.Push("landed", new Dictionary<string, string?> { ["f.txt"] = "2\n" });
        var log = new ListLog();

        new WipSweep(new Clock(Start)).Sweep([Repo(fx, log, "1-A-x", "2-A-y")], Start);

        Assert.False(Exists(fx, "wip/1-A"));
        Assert.True(Exists(fx, "wip/2-A"));
        Assert.True(Exists(fx, "wip/3-A")); // its card is not in done/
        Assert.Contains(log.Lines, l => l.Contains("deleted wip/1-A", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("wip/2-A kept", StringComparison.Ordinal) && l.Contains("new.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void SeveralMergedBranches_AreDeletedInOnePush()
    {
        using var fx = Setup();
        PushBranch(fx, "wip/1-A", new Dictionary<string, string> { ["f.txt"] = "2\n" });
        PushBranch(fx, "wip/2-A", new Dictionary<string, string> { ["f.txt"] = "2\n" });
        fx.Push("landed", new Dictionary<string, string?> { ["f.txt"] = "2\n" });
        var log = new ListLog();

        new WipSweep(new Clock(Start)).Sweep([Repo(fx, log, "1-A-x", "2-A-y")], Start);

        Assert.False(Exists(fx, "wip/1-A"));
        Assert.False(Exists(fx, "wip/2-A"));
        Assert.Single(log.Lines, l => l.Contains("deleted", StringComparison.Ordinal));
    }

    [Fact]
    public void KeptBranch_IsLoggedOncePerDay()
    {
        using var fx = Setup();
        PushBranch(fx, "wip/2-A", new Dictionary<string, string> { ["new.txt"] = "x\n" });
        var log = new ListLog();
        var sweep = new WipSweep(new Clock(Start));
        var repo = Repo(fx, log, "2-A-y");

        sweep.Sweep([repo], Start);
        sweep.Sweep([repo], Start.AddHours(2));
        Assert.Single(log.Lines, l => l.Contains("kept", StringComparison.Ordinal));

        sweep.Sweep([repo], Start.AddDays(1));
        Assert.Equal(2, log.Lines.Count(l => l.Contains("kept", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Tick_RunsOnATaskOfItsOwn_AndOnceADay()
    {
        var clock = new Clock(Start);
        var sweep = new WipSweep(clock);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        IReadOnlyList<SweepRepo> Repos()
        {
            _ = Interlocked.Increment(ref calls);
            release.Wait(TimeSpan.FromSeconds(30));
            return [];
        }

        Assert.True(sweep.Tick(Repos));   // returns while the sweep is still blocked: it never delays the caller
        Assert.False(sweep.Current.IsCompleted);
        Assert.False(sweep.Tick(Repos));  // one at a time
        release.Set();
        await sweep.Current.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(sweep.Tick(Repos));  // not again the same day
        clock.Now = Start.AddHours(25);
        Assert.True(sweep.Tick(Repos));
        await sweep.Current.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ListRemote_ReturnsWipBranchesOnly()
    {
        using var fx = Setup();
        PushBranch(fx, "wip/1-A", new Dictionary<string, string> { ["f.txt"] = "2\n" });
        PushBranch(fx, "other/x", new Dictionary<string, string> { ["f.txt"] = "3\n" });

        var set = new WipBranches(new GitRunner(), "origin", "main").ListRemote(fx.Repo);

        Assert.Equal(["wip/1-A"], set);
    }

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
