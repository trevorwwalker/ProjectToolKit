// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// Card 890-C: the routes WT-P4 gives the worker types page. The old slot controls are gone from the
/// route table, so a POST to them queues nothing; the panel's Save and the per-agent Resume are new.
/// </summary>
public sealed class PageWorkerTypesRouteTests
{
    // WT-P4: /enable, /disable, /slots-add, /slots-remove and /worker-settings reach no action any more.
    [Fact]
    public async Task Page_OldSlotRoutes_Gone()
    {
        using var web = new WebHarness();

        foreach (var path in new[] { "/enable?slot=2", "/disable?slot=2", "/slots-add", "/slots-remove", "/worker-settings?slot=2" })
        {
            var answer = await web.PostFormAsync(path, "slot=2");

            // A POST with no route goes back to the page without queuing anything (PAGE-9).
            Assert.Equal(HttpStatusCode.SeeOther, answer.StatusCode);
        }

        Assert.Equal(0, web.Actions.CallCount("/enable"));
        Assert.Equal(0, web.Actions.CallCount("/disable"));
        Assert.Equal(0, web.Actions.CallCount("/slots-add"));
        Assert.Equal(0, web.Actions.CallCount("/slots-remove"));
        Assert.Equal(0, web.Actions.CallCount("/worker-settings"));
        Assert.Null(web.Actions.LastSlot);
    }

    // WT-P4: the page still has its Stop, Kill and message box, and they reach the contract by slot.
    [Fact]
    public async Task Page_StopKillSend_StillPostBySlot()
    {
        using var web = new WebHarness();

        await web.PostFormAsync("/stop?slot=3");
        await web.PostFormAsync("/kill?slot=4");
        await web.PostFormAsync("/send?slot=5", "text=push%20your%20work");

        Assert.Equal(1, web.Actions.CallCount("/stop"));
        Assert.Equal(1, web.Actions.CallCount("/kill"));
        Assert.Equal(1, web.Actions.CallCount("/send"));
        Assert.Equal(5, web.Actions.LastSlot);
        Assert.Equal("push your work", web.Actions.LastMessage);
    }

    /// <summary>
    /// WT-P3, WT-P4: the panel's Save posts the total and every row, and the route hands them over in
    /// the panel's order with the fields the page carries. A blank row is not a type.
    /// </summary>
    [Fact]
    public async Task Page_WorkerTypes_SaveCarriesTheTotalAndEveryRow()
    {
        using var web = new WebHarness();

        const string Body =
            "maxWorkers=4"
            + "&wname=worker&wmax=2&wagent=copilot&wmodel=spark&wendpoint=http%3A%2F%2Fspark.local%3A18400%2Fv1&wmetrics=http%3A%2F%2Fspark.local%3A9090%2Fmetrics&wareas=abc&wprojects="
            + "&wname=Sonnet&wmax=1&wagent=claude&wmodel=claude-sonnet-5-5&wendpoint=&wmetrics=&wareas=&wprojects=Kestrel%2C+Poolkit"
            + "&wname=Opus&wmax=0&wagent=&wmodel=&wendpoint=&wmetrics=&wareas=&wprojects="
            + "&wname=&wmax=&wagent=&wmodel=&wendpoint=&wmetrics=&wareas=&wprojects=";

        var answer = await web.PostFetchAsync("/worker-types", Body);

        Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
        Assert.Equal(1, web.Actions.CallCount("/worker-types"));
        Assert.Equal(
            "4"
            + "|worker:2:copilot:spark:http://spark.local:18400/v1:http://spark.local:9090/metrics:"
            + ";Sonnet:1:claude:claude-sonnet-5-5:::Kestrel,Poolkit"
            + ";Opus:0:::::",
            web.Actions.LastWorkerTypes);
    }

    // A Save with nothing in it keeps the total at 1 (WT-C1) and brings no types.
    [Fact]
    public async Task Page_WorkerTypes_EmptySave_QueuesNoTypes()
    {
        using var web = new WebHarness();

        await web.PostFormAsync("/worker-types", "maxWorkers=0");

        Assert.Equal("1|", web.Actions.LastWorkerTypes);
    }

    /// <summary>
    /// 947-B: every row also posts the name it was loaded with, in row order, which is what lets the
    /// Save tell a renamed type from one row removed and another added. The row Add type made has none,
    /// because it is a new type.
    /// </summary>
    [Fact]
    public async Task Page_WorkerTypes_SaveCarriesEachRowsLoadedName()
    {
        using var web = new WebHarness();

        const string Body =
            "maxWorkers=4"
            + "&worig=worker&wname=worker&wmax=2"
            + "&worig=Sonnet&wname=Sonnet&wmax=1"
            + "&worig=&wname=Opus&wmax=1";

        var answer = await web.PostFetchAsync("/worker-types", Body);

        Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
        Assert.Equal(
            "4|worker:2:::::;Sonnet:1:::::;Opus:1:::::",
            web.Actions.LastWorkerTypes);
        Assert.Equal("worker;Sonnet;", web.Actions.LastWorkerTypesOriginal);
    }

    /// <summary>1147-C: the Save posts the chosen Default radio as the pool's defaultWorker.</summary>
    [Fact]
    public async Task Page_WorkerTypes_SavePostsTheChosenDefault()
    {
        using var web = new WebHarness();

        await web.PostFetchAsync("/worker-types", "maxWorkers=4&wname=worker&wmax=2&wname=Sonnet&wmax=1&wdefault=Sonnet");

        Assert.Equal("Sonnet", web.Actions.LastDefaultWorker);
    }

    /// <summary>1147-C: a Save that names no default leaves it to the server ("" keeps the current one).</summary>
    [Fact]
    public async Task Page_WorkerTypes_SaveWithNoRadioNamesNoDefault()
    {
        using var web = new WebHarness();

        await web.PostFetchAsync("/worker-types", "maxWorkers=4&wname=worker&wmax=2");

        Assert.Equal("", web.Actions.LastDefaultWorker);
    }

    // WT-9: the Resume beside an out-of-quota agent names that agent.
    [Fact]
    public async Task Page_AgentResume_NamesTheAgent()
    {
        using var web = new WebHarness();

        await web.PostFormAsync("/agent-resume?agent=claude");

        Assert.Equal(1, web.Actions.CallCount("/agent-resume"));
        Assert.Equal("claude", web.Actions.LastAgentResumed);
    }

    /// <summary>
    /// 992-B: the Categories panel's Save posts every row with the letter it was loaded with, its owns
    /// and except one path per line, and the shared add-only list; an empty Add category row is dropped.
    /// </summary>
    [Fact]
    public async Task Page_Categories_SaveCarriesEveryRowAndTheSharedList()
    {
        using var web = new WebHarness();

        const string Body =
            "corig=A&cletter=A&cname=core&cpurpose=logic&ccolor=%23336699&cowns=src%2Fa%2F%0D%0Atest%2Fa%2F%0D%0A&cexcept=src%2Fa%2Fweb%2F"
            + "&corig=B&cletter=E&cname=host&cpurpose=&ccolor=&cowns=src%2Fb%2F&cexcept="
            + "&corig=&cletter=&cname=&cpurpose=&ccolor=&cowns=&cexcept="
            + "&addonly=.gitignore%0D%0Adocs%2Fknown_gaps.md%0D%0A";

        var answer = await web.PostFetchAsync("/categories", Body);

        Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
        Assert.Equal(1, web.Actions.CallCount("/categories"));
        Assert.Equal("A(A):core:logic:#336699:src/a/,test/a/|src/a/web/;E(B):host:::src/b/|", web.Actions.LastCategories);
        Assert.Equal(".gitignore,docs/known_gaps.md", web.Actions.LastAddOnly);
    }
}
