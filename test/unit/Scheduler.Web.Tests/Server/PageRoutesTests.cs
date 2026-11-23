using System.Net;
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

public class PageRouteTests
{
    // PAGE-9: every GET route of Handle answers 200 with valid input, /log never leaves the log
    // folder and gives at most 2000 ANSI-stripped lines, an unknown path is 404, /pause only acts
    // when running and /resume only when paused, and every POST route reaches its handler.
    [Fact]
    public async Task Page_Routes_GetAndPost()
    {
        using var web = new WebHarness();
        web.Data.FontPath = web.WriteLog("page.woff2", "font bytes");
        web.Data.Cards["050-C-web-server-and-routes.md"] = "the card text";
        web.Data.Requests["12-request.md"] = "the request text";
        web.Data.Health = "main is green at 12:00";
        web.WriteLog("050-C-20261118-120000.log", "worker output\n");
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("051-C-20261118-120001.log", "slot one log\n"), "stats", "time"));

        var gets = new[]
        {
            "/",
            "/font",
            "/log?file=050-C-20261118-120000.log",
            "/log?recent=0",
            "/log?slot=1",
            "/card?name=050-C-web-server-and-routes.md",
            "/request?name=12-request.md",
            "/health",
            "/live.json",
            "/status.json",
        };
        foreach (var path in gets)
        {
            var response = await web.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var font = await web.Client.GetAsync("/font");
        Assert.Equal("font/woff2", font.Content.Headers.ContentType?.ToString());
        Assert.Equal("font bytes", await font.Content.ReadAsStringAsync());

        // A name that is not <id>-<ts>.log never reaches outside the log folder.
        foreach (var bad in new[] { @"..%5C..%5Cwindows%5Cwin.ini", "..%5Fx.log", "050-C-20261118-120000.log.txt", "%2Fetc%2Fpasswd" })
        {
            var escape = await web.Client.GetAsync($"/log?file={bad}");
            Assert.True(
                escape.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
                $"escape attempt {bad} answered {(int)escape.StatusCode}");
        }

        // /log gives at most the last 2000 lines with the ANSI marks stripped.
        var big = new StringBuilder();
        for (var n = 1; n <= 2500; n++)
        {
            big.Append(n % 100 == 0 ? $"\x1b[31mline {n}\x1b[0m" : $"line {n}").Append('\n');
        }

        web.WriteLog("052-C-20261118-120002.log", big.ToString());
        var tail = await web.Client.GetAsync("/log?file=052-C-20261118-120002.log");
        Assert.Equal(HttpStatusCode.OK, tail.StatusCode);
        var text = await tail.Content.ReadAsStringAsync();
        var lines = text.Split('\n');
        Assert.Equal(2000, lines.Length);
        Assert.Equal("line 501", lines[0]);
        Assert.Equal("line 2500", lines[1999]);
        Assert.False(text.Contains("\x1b", StringComparison.Ordinal));

        // A log that is not there is reported, and an unknown path is 404.
        var unknown = await web.Client.GetAsync("/no-such-route");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Every other POST route of PAGE-9 reaches its handler and gets the form answer.
        var posts = new[]
        {
            "/pause", "/resume", "/health-now", "/drain", "/planner-toggle", "/stopall", "/tick",
            "/send?slot=1", "/answer?card=050-C.md", "/open-lanes",
            "/open-workers", "/accept-lane?lane=C", "/open?card=050-C.md", "/resume-card?card=050-C.md",
            "/done?card=050-C.md", "/retry?card=050-C.md",
            "/stop?slot=2", "/kill?slot=2", "/worker-types", "/agent-resume?agent=claude",
        };
        foreach (var path in posts)
        {
            var posted = await web.PostFormAsync(path);
            Assert.Equal(HttpStatusCode.SeeOther, posted.StatusCode);
            Assert.Equal("/", posted.Headers.Location?.ToString());
        }

        foreach (var path in posts)
        {
            Assert.Equal(1, web.Actions.CallCount(path.Split('?')[0]));
        }
    }

    // PAGE-9: the arguments a POST carries reach the handler; the form and the query both count.
    [Fact]
    public async Task Page_PostArguments_ReachTheHandler()
    {
        using var web = new WebHarness();

        await web.PostFormAsync("/send?slot=2", "text=hello%20%20there");
        Assert.Equal(2, web.Actions.LastSlot);
        Assert.Equal("hello there", web.Actions.LastMessage);

        await web.PostFormAsync("/answer?card=050-C.md", "q=1&a=2&note=ok%20thanks");
        Assert.Equal("050-C.md|1|2|ok thanks", web.Actions.LastAnswer);

        // The page's answer buttons carry the question and the choice in the query (2026-11-18: they
        // sent question= and no choice, so every answer was refused as "no open question Q-1").
        await web.PostFormAsync("/answer?card=440-A&q=2&a=1");
        Assert.Equal("440-A|2|1|", web.Actions.LastAnswer);

        await web.PostFormAsync("/accept-lane?lane=C");
        Assert.Equal("C", web.Actions.LastCategory);

        await web.PostFormAsync("/done?card=050-C.md");
        Assert.Equal("050-C.md", web.Actions.LastCard);

        // WT-P4: the slot controls that are left reach the contract by slot.
        await web.PostFormAsync("/stop?slot=2");
        Assert.Equal(2, web.Actions.LastSlot);
    }

    // PAGE-9: /pause only changes the state while running, /resume only while paused*.
    [Fact]
    public async Task Page_PauseAndResume_GuardedByState()
    {
        using var running = new WebHarness();
        await running.PostFormAsync("/pause");
        Assert.Equal(SchedulerState.Paused, running.Actions.State);

        using var draining = new WebHarness();
        draining.Actions.State = SchedulerState.Draining;
        await draining.PostFormAsync("/pause");
        Assert.Equal(SchedulerState.Draining, draining.Actions.State);
        await draining.PostFormAsync("/resume");
        Assert.Equal(SchedulerState.Draining, draining.Actions.State);

        using var pausedRed = new WebHarness();
        pausedRed.Actions.State = SchedulerState.PausedRed;
        await pausedRed.PostFormAsync("/resume");
        Assert.Equal(SchedulerState.Running, pausedRed.Actions.State);
    }
}
