// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// 957-B: a click is answered at once and its work runs behind it. The router validates the request
/// and either runs a pure state action there and then or puts the work on the queue's one worker
/// thread, which takes the clicks in order and runs one at a time so no two commits overlap. The
/// live feed carries how far each action got - queued, running, done, failed with its message - so
/// the page can show the click's effect at once and put the item back with the reason if it fails.
/// </summary>
public class PageInstantActionTests
{
    // 957-B item 1: the reply does not wait for the work. The answer's commit is held back on a gate;
    // the POST is answered while it is still held, and the work follows when the gate opens.
    [Fact]
    public async Task Page_InstantAction_ReplyDoesNotWaitForTheCommit()
    {
        using var web = new WebHarness();
        using var busy = new ManualResetEventSlim(false);
        web.Actions.BlockOn = busy;
        web.Actions.Block.Add("/answer");

        var post = web.PostFetchAsync("/answer?card=955-D&q=1&a=0", drain: false);
        var response = await Finished(post, "the click waited for its git work to finish");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("""{"id":1}""", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, web.Actions.CallCount("/answer"));

        busy.Set();
        web.WaitActions();
        Assert.Equal(1, web.Actions.CallCount("/answer"));
        Assert.Equal("955-D|1|0|", web.Actions.LastAnswer);
    }

    // 957-B item 1 and 5: one queue, in click order, one action at a time. Three answers to one card
    // are posted while the first is still working and reach the scheduler in the order clicked.
    [Fact]
    public async Task Page_InstantAction_ClicksRunInOrderOneAtATime()
    {
        using var web = new WebHarness();
        using var busy = new ManualResetEventSlim(false);
        web.Actions.BlockOn = busy;
        web.Actions.Block.Add("/answer");

        var first = web.PostFetchAsync("/answer?card=955-D&q=1&a=0", drain: false);
        await Finished(first);
        var second = web.PostFetchAsync("/answer?card=955-D&q=2&a=1", "note=second", drain: false);
        await Finished(second);
        var third = web.PostFetchAsync("/answer?card=955-D&q=3&a=1", "note=third", drain: false);
        await Finished(third);

        // Nothing has run yet: the worker thread is sitting in the first answer.
        Assert.Equal(0, web.Actions.CallCount("/answer"));
        busy.Set();
        web.WaitActions();

        Assert.Equal("/answer,/answer,/answer", web.Actions.Ran());
        Assert.Equal(
            [
                "955-D|1|0|",
                "955-D|2|1|second",
                "955-D|3|1|third",
            ],
            web.Actions.Answers);
    }

    // 957-B item 1: a form POST is answered straight away too, and ids count the clicks in the order
    // they were taken.
    [Fact]
    public async Task Page_InstantAction_IdsFollowTheClicks()
    {
        using var web = new WebHarness();

        var one = await web.PostFetchAsync("/done?card=955-D");
        var two = await web.PostFormAsync("/done?card=956-A");
        Assert.Equal("""{"id":1}""", await one.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.SeeOther, two.StatusCode);
        Assert.Equal(2, web.Actions.CallCount("/done"));
    }

    // 957-B item 3: /live.json carries each queued action's state, so the page can replace the mark
    // the click made when the work really lands.
    [Fact]
    public async Task Live_Json_CarriesTheQueuedActionsFromQueuedToDone()
    {
        using var web = new WebHarness();
        using var busy = new ManualResetEventSlim(false);
        web.Actions.BlockOn = busy;
        web.Actions.Block.Add("/done");

        var post = web.PostFetchAsync("/done?card=955-D", drain: false);
        await Finished(post);

        var waiting = await GetLive(web);
        // The worker takes the click off the queue at once, so what the page sees while the commit is
        // held back is the action on its way, not a terminal one.
        Assert.Contains(StatesOf(waiting), state => state is "queued" or "running");
        Assert.Contains("955-D", ActionsOf(waiting), StringComparison.Ordinal);
        Assert.DoesNotContain("done", StatesOf(waiting));

        busy.Set();
        web.WaitActions();
        var landed = StatesOf(await GetLive(web));
        Assert.Contains("done", landed);
        Assert.DoesNotContain("queued", landed);
    }

    // 957-B item 3: an action that fails leaves the queue with the scheduler's reason, which is what
    // the page's notice line shows instead of the item silently coming back.
    [Fact]
    public async Task Live_Json_CarriesAFailedActionWithItsMessage()
    {
        using var web = new WebHarness();
        web.Actions.Fails["/answer"] = "955-D Q2: already answered";

        await web.PostFetchAsync("/answer?card=955-D&q=2&a=1");

        var live = ActionsOf(await GetLive(web));
        Assert.Contains("failed", live, StringComparison.Ordinal);
        Assert.Contains("already answered", live, StringComparison.Ordinal);
        Assert.Contains("answer", live, StringComparison.Ordinal);
    }

    // 957-B item 4: a pure state action is applied to the in-memory state before its reply goes out,
    // so it does not wait behind the commit another click is busy with.
    [Fact]
    public async Task Page_FastPath_AppliesWhileTheQueueIsBusy()
    {
        using var web = new WebHarness();
        using var busy = new ManualResetEventSlim(false);
        web.Actions.BlockOn = busy;
        web.Actions.Block.Add("/answer");

        var answer = web.PostFetchAsync("/answer?card=955-D&q=1&a=0", drain: false);
        await Finished(answer);
        Assert.Equal(0, web.Actions.CallCount("/answer"));

        var pause = await Finished(web.PostFetchAsync("/pause", drain: false), "Pause waited behind the answer's commit");
        Assert.Equal(HttpStatusCode.Accepted, pause.StatusCode);
        Assert.Equal(SchedulerState.Paused, web.Actions.State);

        busy.Set();
        web.WaitActions();
        Assert.Equal(1, web.Actions.CallCount("/answer"));
    }

    // 957-B item 1: the router checks the request before it queues anything, so a click that names no
    // card never reaches the scheduler and says why instead.
    [Fact]
    public async Task Page_InstantAction_NoCardNamed_IsRefusedAtTheDoor()
    {
        using var web = new WebHarness();

        await web.PostFetchAsync("/done");
        Assert.Equal(0, web.Actions.CallCount("/done"));

        var live = ActionsOf(await GetLive(web));
        Assert.Contains("failed", live, StringComparison.Ordinal);
        Assert.Contains("no card was named", live, StringComparison.Ordinal);
    }

    // 957-B: the queue's one worker thread is what keeps two commits from overlapping: an action is
    // never running while another started.
    [Fact]
    public void PageActionQueue_RunsOneAtATimeInOrder()
    {
        using var queue = new Scheduler.Web.Server.PageActionQueue();
        var inFlight = 0;
        var overlapped = false;

        for (var n = 1; n <= 4; n++)
        {
            var captured = n;
            queue.Enqueue("test", "card-" + captured, () =>
            {
                if (Interlocked.Increment(ref inFlight) > 1)
                {
                    overlapped = true;
                }

                Thread.Sleep(10);
                Interlocked.Decrement(ref inFlight);
                return Scheduler.Core.WebContract.ActionResult.Queued(captured);
            });
        }

        Assert.True(queue.WaitIdle(TimeSpan.FromSeconds(10)), "the queue never finished its work");
        Assert.False(overlapped, "two page actions ran at once");

        var states = queue.States();
        Assert.Equal(4, states.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, states.Select(state => state.Id).ToArray());
        Assert.All(states, state => Assert.Equal("done", state.State));
    }

    // 957-B: stopping a queue whose worker thread had already finished must not throw. The worker
    // exits as soon as it is told to stop, and reading a property of a dead thread - which Dispose
    // did - is a ThreadStateException; it surfaced as a page whose teardown sometimes failed.
    [Fact]
    public void PageActionQueue_DisposeAfterItsWorkFinished_DoesNotThrow()
    {
        for (var n = 0; n < 50; n++)
        {
            var queue = new Scheduler.Web.Server.PageActionQueue();
            queue.Enqueue("test", "card-" + n, () => Scheduler.Core.WebContract.ActionResult.Queued(n));
            Assert.True(queue.WaitIdle(TimeSpan.FromSeconds(10)), "the queue never finished its work");
            queue.Dispose();
            queue.Dispose(); // a page torn down twice
        }
    }

    // A POST that must be answered while its work is still held back: it returns when the reply is
    // there, and says what was waited for. The work itself is drained separately.
    private static async Task<HttpResponseMessage> Finished(Task<HttpResponseMessage> post, string why = "the click was not answered")
    {
        var done = await Task.WhenAny(post, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(post, done);
        return await post;
    }

    private static async Task<string> GetLive(WebHarness web)
    {
        var response = await web.Client.GetAsync("/live.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // The actions part of the live JSON as text, for the words it carries, and the states on their
    // own, so a route named "done" is not mistaken for an action that has landed.
    private static string ActionsOf(string live)
    {
        using var doc = JsonDocument.Parse(live);
        return doc.RootElement.GetProperty("actions").ToString();
    }

    private static List<string> StatesOf(string live)
    {
        using var doc = JsonDocument.Parse(live);
        return [.. doc.RootElement.GetProperty("actions").EnumerateArray()
            .Select(action => action.GetProperty("state").GetString() ?? string.Empty)];
    }
}


