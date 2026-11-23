using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Tests.MainModel;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Host.PageActions;
using Scheduler.Host.Views;
using Scheduler.Web.Server;

namespace Scheduler.Host.Tests.Views;

/// <summary>
/// 1090-B (views.md VW-20 to VW-22): the views source builds a project's views snapshot from the model of main
/// alone - open cards, done headers, plan files by blob under jobs/ and as watched paths elsewhere, the roadmap -
/// and the page's slots; it rebuilds only when the model's commit, its done-loaded state or the running cards move,
/// and starts no git process (it is given nothing but the model, as MM-15).
/// </summary>
public sealed class ViewsSourceTests
{
    private const string Roadmap = ViewsSource.Roadmap;
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 14, 0, 0, TimeSpan.Zero);

    private static string CardOf(string title, string category, string status, string plan) =>
        $"# {title}\n\nCategory: {category}\nType: feature\nStatus: {status}\nPlan: {plan}\n\n## Task\n\nDo it.\n\n## Result\n";

    private static Dictionary<string, string> Seed() => new(StringComparer.Ordinal)
    {
        ["jobs/queue/10-A-alpha-one.md"] = CardOf("10-A: Alpha one", "A", "ready", "jobs/plans/alpha_plan.md step 1"),
        ["jobs/active/11-B-doc-two.md"] = CardOf("11-B: Doc two", "B", "ready", "docs/design/doc.md section 2"),
        ["jobs/done/9-A-old-three.md"] = CardOf("9-A: Old three", "A", "done", "docs/jobs/plans/old_plan.md (5.2)"),
        ["jobs/done/8-C-gone.md"] = CardOf("8-C: Gone", "C", "done", "docs/gone.md"),
        ["jobs/done/7-A-none.md"] = CardOf("7-A: None", "A", "done", "none (developer, 2026-11-01)"),
        ["jobs/plans/alpha_plan.md"] = "# Alpha plan\n\nStatus: accepted (developer, 2026-11-20)\n",
        ["jobs/plans/beta_plan.md"] = "# Beta plan\n\nNo status here.\n",
        ["jobs/archive/old_plan.md"] = "# Old plan\n\n**Status: finished**\n",
        ["docs/design/doc.md"] = "# A design\n\nRoadmap: item 2\n",
        [Roadmap] = "# Roadmap\n\n## 2. Page views\n\n*Status 2026-11-19: being built*\n",
    };

    private static List<PageSlotView> Slots(params string[] cards) =>
        [.. cards.Select((c, i) => new PageSlotView { Slot = i + 1, Card = c })];

    [Fact]
    public void VW_21_SnapshotFromTheModel_OpenAndDoneCards_PlanFilesAndRoadmap()
    {
        var main = new FakeMain(Seed());
        var source = new ViewsSource("ptk", "jobs", () => main, () => Slots("11-B-doc-two"));

        Assert.True(source.Refresh());
        var views = source.Current;

        Assert.Equal("ptk", views.Project);
        Assert.True(views.DoneLoaded);
        Assert.Equal("", views.Note);
        Assert.Equal(main.Commit, views.Commit);
        Assert.Equal(["7-A", "8-C", "9-A", "10-A", "11-B"], views.Snapshot.Cards.Select(c => c.Id));
        Assert.Equal(ViewFolder.Done, views.Snapshot.Cards.Single(c => c.Id == "9-A").Folder);
        Assert.EndsWith("Alpha one", views.Snapshot.Cards.Single(c => c.Id == "10-A").Title, StringComparison.Ordinal);
        Assert.Equal(["11-B"], views.Running);

        var plans = views.Snapshot.Plans.ToDictionary(p => p.Path);
        Assert.Equal(PlanKind.Plans, plans["jobs/plans/alpha_plan.md"].Kind);
        Assert.Equal("Alpha plan", plans["jobs/plans/alpha_plan.md"].Title);
        Assert.Equal("Status: accepted (developer, 2026-11-20)", plans["jobs/plans/alpha_plan.md"].Status);
        Assert.Equal(PlanStatus.None, plans["jobs/plans/beta_plan.md"].Status);     // VW-11: no card names it
        Assert.Equal(PlanKind.Archived, plans["jobs/archive/old_plan.md"].Kind);     // VW-12: docs/jobs/ and archive
        Assert.Equal(PlanKind.Elsewhere, plans["docs/design/doc.md"].Kind);
        Assert.Equal("A design", plans["docs/design/doc.md"].Title);
        Assert.Equal(PlanKind.Missing, plans["docs/gone.md"].Kind);
        Assert.Equal(PlanKind.Elsewhere, plans[ViewsSource.Roadmap].Kind);          // 1114-C: the roadmap is one plan, its items the steps
        Assert.Contains("Page views", views.Snapshot.PlanTexts[ViewsSource.Roadmap], StringComparison.Ordinal);
        Assert.StartsWith("# Alpha plan", views.Snapshot.PlanTexts["jobs/plans/alpha_plan.md"], StringComparison.Ordinal);
        Assert.DoesNotContain(views.Snapshot.Plans, p => p.Path.StartsWith("Roadmap item", StringComparison.Ordinal));
        Assert.Equal(2, views.RoadmapItemOf["docs/design/doc.md"]);
        Assert.DoesNotContain(views.Snapshot.Plans, p => p.Kind == PlanKind.NoPlan);

        // What the page needs to count them (1091-C): the counts come out of the snapshot and its sets alone.
        var counts = PlanCounts.Of(views.Snapshot.Cards, new HashSet<string>(views.Running), new HashSet<string>(views.OnMain));
        var doc = counts.Single(c => c.Plan.Path == "docs/design/doc.md");
        Assert.Equal(1, doc.Tally.Running);
        Assert.Equal(1, counts.Single(c => c.Plan.Path == "jobs/archive/old_plan.md").Tally.Done);
    }

    [Fact]
    public void VW_22_RebuiltOnlyWhenTheCommitOrTheRunningCardsMove_NoReadsOtherwise()
    {
        var main = new FakeMain(Seed());
        var running = Slots();
        var source = new ViewsSource("ptk", "jobs", () => main, () => running);
        Assert.True(source.Refresh());
        var first = source.Current;

        main.Objects.ResetCounts();
        Assert.False(source.Refresh());
        Assert.False(source.Refresh());
        Assert.Equal(1, source.Builds);
        Assert.Same(first, source.Current);
        Assert.Equal(0, main.Objects.Reads);

        // A worker starts: rebuilt, the cards unchanged, so the version the page compares stays.
        running = Slots("10-A-alpha-one");
        Assert.True(source.Refresh());
        Assert.Equal(2, source.Builds);
        Assert.Equal(["10-A"], source.Current.Running);
        Assert.Equal(first.Version, source.Current.Version);

        // A push that moves a card: rebuilt, and the version moves; the unchanged plan blobs are not read again.
        main.Objects.ResetCounts();
        _ = main.Move("jobs/queue/10-A-alpha-one.md", "jobs/done/10-A-alpha-one.md");
        Assert.True(source.Refresh());
        Assert.Equal(3, source.Builds);
        Assert.Equal(ViewFolder.Done, source.Current.Snapshot.Cards.Single(c => c.Id == "10-A").Folder);
        Assert.NotEqual(first.Version, source.Current.Version);
        Assert.DoesNotContain(main.Objects.BlobReads, b => b == FakeObjects.BlobId(Seed()["jobs/plans/alpha_plan.md"]));
        Assert.False(source.Refresh());
    }

    [Fact]
    public void VW_21_BeforeTheDoneHeaders_SaysReading_AndBeforeTheModel_SaysStarting()
    {
        IMainModel? model = null;
        var source = new ViewsSource("ptk", "jobs", () => model, () => Slots());
        Assert.False(source.Refresh());
        Assert.Equal(ProjectViews.Starting, source.Current.Note);
        Assert.Empty(source.Current.Snapshot.Cards);

        var objects = new FakeObjects();
        var repo = new RepositoryModel(objects, new MainLayout(["jobs"]), new FakeMain.FixedClock(Now));
        Assert.True(repo.Load(objects.Commit(Seed(), null, "root", Now)));
        model = repo;
        Assert.True(source.Refresh());
        Assert.False(source.Current.DoneLoaded);
        Assert.Equal(ProjectViews.ReadingDone, source.Current.Note);
        Assert.Equal(["10-A", "11-B"], source.Current.Snapshot.Cards.Select(c => c.Id));
        var reading = source.Current.Version;

        // Phase 2 ends at the same commit: rebuilt with the done cards.
        Assert.True(repo.LoadDone());
        Assert.True(source.Refresh());
        Assert.True(source.Current.DoneLoaded);
        Assert.Equal("", source.Current.Note);
        Assert.Equal(5, source.Current.Snapshot.Cards.Count);
        Assert.NotEqual(reading, source.Current.Version);
    }

    [Fact]
    public void MM_15_TheSourceIsGivenOnlyTheModel_NoGitReader()
    {
        // Every way in is the model, the slots or plain text: there is no git reader or process runner to call.
        var types = typeof(ViewsSource).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Equal([typeof(string), typeof(string), typeof(Func<IMainModel?>), typeof(Func<IReadOnlyList<PageSlotView>>), typeof(string)], types);
    }

    [Fact]
    public void RunningIds_TakeTheIdFromAFullCardName_IdleSlotsLeftOut()
    {
        var ids = ViewsSource.RunningIds([.. Slots("1090-B-plans-view-source", "", "77-C")]);
        Assert.Equal(["1090-B", "77-C"], ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void VW_20_LiveJsonCarriesTheViewsVersion_OutOfThePageVersion()
    {
        var main = new FakeMain(Seed());
        var source = new ViewsSource("ptk", "jobs", () => main, () => Slots());
        _ = source.Refresh();
        var pool = new PoolPageSnapshot { Built = Now };
        var data = new PoolPageData(main.Clock, Path.GetTempPath(), () => [], () => pool with { Views = [source.Current] });
        var feed = new LiveFeed(main.Clock, data);

        feed.Build();
        var views = ProjectViews.VersionOf([source.Current]);
        Assert.NotEqual("", views);
        Assert.Equal(views, feed.Snapshot.Views);
        Assert.Contains($"\"views\":\"{views}\"", feed.Json, StringComparison.Ordinal);
        var page = PageVersion.Of(data.PoolSnapshot().Pages);

        // A card moved: the views version moves, the page's does not.
        _ = main.Move("jobs/queue/10-A-alpha-one.md", "jobs/review/10-A-alpha-one.md");
        Assert.True(source.Refresh());
        data.Refresh();
        feed.Build();
        Assert.NotEqual(views, feed.Snapshot.Views);
        Assert.Equal(page, PageVersion.Of(data.PoolSnapshot().Pages));
        Assert.Contains($"\"page\":\"{page}\"", feed.Json, StringComparison.Ordinal);
    }
}
