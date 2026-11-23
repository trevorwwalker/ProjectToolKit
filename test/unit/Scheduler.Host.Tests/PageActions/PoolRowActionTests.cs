using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Host.Moves;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// Card 1004-C: the actions behind a pool page row's buttons, on one project's own page actions -
/// Categories opens that project's categories file through the launcher that raises the editor
/// (categories.json, or lanes.json when only that exists), and Stop asks that project's workers to
/// stop and pauses it without draining, so the scheduler does not exit.
/// </summary>
public class PoolRowActionTests
{
    private static string OnDisk(PageActionHarness harness, string name)
    {
        var file = Path.Combine(harness.Config.Repo, "docs", "jobs", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{}");
        return file;
    }

    // Categories: categories.json when it is on disk (1007-C: on disk, not on main).
    [Fact]
    public void Card1007_Categories_OpensCategoriesJsonWhenOnDisk()
    {
        var launcher = new Launcher();
        using var harness = new PageActionHarness(launcher: launcher);
        var categories = OnDisk(harness, "categories.json");
        OnDisk(harness, "lanes.json");

        Assert.True(harness.Actions.OpenCategories().Ok);

        Assert.Equal([categories], launcher.Opened);
    }

    // Categories: lanes.json when that is the only one on disk, even though main already has categories.json.
    [Fact]
    public void Card1007_Categories_OpensLanesJsonWhenOnlyThatIsOnDisk()
    {
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files["docs/jobs/categories.json"] = "{}";
        var launcher = new Launcher();
        using var harness = new PageActionHarness(main: main, launcher: launcher);
        var lanes = OnDisk(harness, "lanes.json");

        Assert.True(harness.Actions.OpenCategories().Ok);

        Assert.Equal([lanes], launcher.Opened);
    }

    // Categories: neither on disk -> refused, naming both paths, nothing opened.
    [Fact]
    public void Card1007_Categories_NeitherOnDisk_RefusedNamingBoth()
    {
        var launcher = new Launcher();
        using var harness = new PageActionHarness(launcher: launcher);

        var answer = harness.Actions.OpenCategories();

        Assert.False(answer.Ok);
        Assert.Empty(launcher.Opened);
        var message = Assert.Single(harness.Notices);
        Assert.Contains(Path.Combine("docs", "jobs", "categories.json"), message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("docs", "jobs", "lanes.json"), message, StringComparison.Ordinal);
    }

    // Stop for one project: every running worker gets the page's grace and its stop flag, the project
    // pauses, and it does not drain (Stop all's drain is what ends the scheduler).
    [Fact]
    public void Card1004_StopWorkers_StopsEachWorker_PausesWithoutDraining()
    {
        using var harness = new PageActionHarness(project: "ptk-1004c-stop");
        harness.Running(1);
        harness.Running(2, card: "011-B-y", category: "B");

        Assert.True(harness.Actions.StopWorkers().Ok);

        Assert.Equal(SchedulerState.Paused, harness.Tick.State);
        foreach (var slot in new[] { 1, 2 })
        {
            var worker = harness.Tick.Running[slot];
            Assert.Equal("stopped from the pool page", worker.StopWhy);
            Assert.Equal(5, worker.StopGraceMinutes);
            Assert.True(File.Exists(WorkerFiles.StopFlag(harness.LogDir, slot)), "no stop flag for slot " + slot);
        }

        Assert.Contains(harness.Log.Lines, line => line.Contains("stop from the pool page", StringComparison.Ordinal));
    }

    private sealed class Launcher : IFileLauncher
    {
        public List<string> Opened { get; } = [];

        public void Open(string file) => Opened.Add(file);
    }
}
