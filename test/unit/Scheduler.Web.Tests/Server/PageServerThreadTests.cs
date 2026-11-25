// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

public class PageServerThreadTests
{
    // PAGE-1: the page thread answers /live.json and / from the snapshots and queues every POST
    // (202 {"id":n} for a fetch, 303 for a form); the main loop is not needed for any of it.
    [Fact]
    public async Task Page_ServerThread_SnapshotsAndPostQueue()
    {
        using var web = new WebHarness();
        web.Renderer.Html = "<html>the page from its snapshot</html>";
        using var busy = new ManualResetEventSlim(false);
        web.Actions.BlockOn = busy;

        // A fetch POST while the loop is stuck: the request waits, the page thread must not.
        var fetch = web.PostFetchAsync("/tick");

        var live = await web.Client.GetAsync("/live.json");
        var home = await web.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("application/json; charset=utf-8", live.Content.Headers.ContentType?.ToString());
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("the page from its snapshot", await home.Content.ReadAsStringAsync());
        Assert.Equal(0, web.Actions.CallCount("/tick"));

        busy.Set();
        var fetchResponse = await fetch;
        Assert.Equal(HttpStatusCode.Accepted, fetchResponse.StatusCode);
        Assert.Equal("application/json; charset=utf-8", fetchResponse.Content.Headers.ContentType?.ToString());
        Assert.Equal("""{"id":1}""", await fetchResponse.Content.ReadAsStringAsync());

        // The same route as a plain form: 303 back to the page, and queued all the same.
        var form = await web.PostFormAsync("/tick");
        Assert.Equal(HttpStatusCode.SeeOther, form.StatusCode);
        Assert.Equal("/", form.Headers.Location?.ToString());
        Assert.Equal(2, web.Actions.CallCount("/tick"));

        // Accept: application/json is the other way the page script asks for the id.
        var accepted = await web.PostJsonAcceptAsync("/tick");
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal("""{"id":3}""", await accepted.Content.ReadAsStringAsync());
        Assert.Equal(3, web.Actions.CallCount("/tick"));
    }
}
