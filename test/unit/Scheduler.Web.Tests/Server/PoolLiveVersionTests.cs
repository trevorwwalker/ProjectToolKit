// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// PAGE-3 (470-C) in a pool, card 1004-C (the developer, 2026-11-20 18:49 items 5, 7, 9, 10, 11 and 13b):
/// the pool page refreshes itself when /live.json's <c>page</c> differs from the version it was drawn
/// from. The pool's feed reported one project's version while the page carried every project's, so
/// they never matched and the page was rebuilt on every poll - the open project dropdown flashed shut,
/// Worker types closed again, answered questions came back, and links, Log and Minimize were clicked
/// on rows already replaced. The feed now answers with the version the pool page itself carries.
/// </summary>
public sealed class PoolLiveVersionTests
{
    private static readonly DateTimeOffset Built = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Card1004_PoolFeed_PageVersion_IsThePoolPagesOwn_AndChangesWithAProject()
    {
        var data = new PoolData { Pool = Pool(Project("AmigaX", "main 1a2b3c4 green at 17:50"), Project("ProjectToolkit", "main not checked yet")) };
        var feed = new LiveFeed(new TestClock(), data);

        var page = PageVersionOf(new CombinedPageRenderer().Render(data.Pool, remote: false, controlsAllowed: true));
        Assert.Equal(page, FeedVersion(feed));

        // A later tick with nothing new (a new snapshot, the same content) keeps it: no refresh.
        data.Pool = Pool(Project("AmigaX", "main 1a2b3c4 green at 17:50"), Project("ProjectToolkit", "main not checked yet")) with { Built = Built.AddSeconds(5) };
        Assert.Equal(page, FeedVersion(feed));

        // One project's page changing is a new version, and it is the page's again.
        data.Pool = Pool(Project("AmigaX", "main 1a2b3c4 green at 17:50"), Project("ProjectToolkit", "main 98a9b1a green at 18:01"));
        var changed = FeedVersion(feed);
        Assert.NotEqual(page, changed);
        Assert.Equal(PageVersionOf(new CombinedPageRenderer().Render(data.Pool, remote: false, controlsAllowed: true)), changed);
    }

    private static string FeedVersion(LiveFeed feed) =>
        JsonDocument.Parse(feed.Answer(null)).RootElement.GetProperty("page").GetString() ?? "";

    private static string PageVersionOf(string html) =>
        Regex.Match(html, "<meta name='page-version' content='([^']*)'>").Groups[1].Value;

    private static PoolPageSnapshot Pool(params PageSnapshot[] pages) => new()
    {
        Built = Built,
        Projects = [.. pages.Select(page => new PoolProjectView { Name = page.Project })],
        Pages = pages,
    };

    private static PageSnapshot Project(string name, string health) => new()
    {
        Project = name,
        Built = Built,
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
        Health = new PageHealthView { Green = true, Text = health },
    };

    private sealed class PoolData : IPoolPageData
    {
        public PoolPageSnapshot Pool { get; set; } = new() { Built = Built };

        public string? FontPath => null;

        public string LogDirectory => "";

        public PoolPageSnapshot PoolSnapshot() => Pool;

        public IPageData? For(string project) => null;

        // What the pool's data hands a one-project caller: the newest project's page, never the pool page's version.
        public PageSnapshot Snapshot() => Pool.Pages.OrderByDescending(page => page.Built).First();

        public string? Card(string name) => null;

        public string? Request(string name) => null;

        public string? HealthText() => null;

        public string StatusJson() => "{}";

        public IReadOnlyList<LiveWorker> RunningWorkers() => [];
    }
}
