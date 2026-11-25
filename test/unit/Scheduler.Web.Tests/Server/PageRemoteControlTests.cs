// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

public class PageRemoteControlTests
{
    private const string OnlyOnThePc = "(Only on the PC running the scheduler.)";

    // PAGE-1, PAGE-7: a POST from another machine is refused with 403 unless remoteControl is on,
    // and the page it shows has its buttons disabled with the reason.
    [Fact]
    public async Task Page_RemotePost_WithoutRemoteControl_403()
    {
        const string otherPc = "192.0.2.31";

        using (var off = new WebHarness(remoteControl: false, remoteAddress: otherPc))
        {
            var post = await off.PostFormAsync("/tick");
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
            Assert.Contains("only on the PC running the scheduler", await post.Content.ReadAsStringAsync());
            Assert.Equal(0, off.Actions.CallCount("/tick"));

            var home = await off.Client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Contains(OnlyOnThePc, await home.Content.ReadAsStringAsync());
            Assert.Equal((true, false), off.Renderer.Last);

            // A GET is still allowed, and the other GET routes answer too.
            var live = await off.Client.GetAsync("/live.json");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        using (var on = new WebHarness(remoteControl: true, remoteAddress: otherPc))
        {
            var post = await on.PostFormAsync("/tick");
            Assert.Equal(HttpStatusCode.SeeOther, post.StatusCode);
            Assert.Equal(1, on.Actions.CallCount("/tick"));

            var home = await on.Client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.DoesNotContain(OnlyOnThePc, await home.Content.ReadAsStringAsync());
            Assert.Equal((true, true), on.Renderer.Last);
        }
    }
}
