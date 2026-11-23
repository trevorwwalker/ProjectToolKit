using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 1103-C (PAGE-3): the page takes its live view from the stream. 1101-B builds a frame of what
/// changed and 1102-C pushes it at up to 24 a second; the script here opens one
/// <c>EventSource('/live/stream')</c> per open page and applies each frame with the code that applies
/// <c>/live.json</c> today - the same window, the same 500-line cap and scroll pinning (490-C, 610-C),
/// the same keep-last-value rule (1092-C) and the same clearing of a window whose slot moved to
/// another card (1093-C, which is what a frame's <c>reset</c> says). On a frame whose page version
/// moved the structure is redrawn with today's guards. When the stream errors or closes the page falls
/// back to the <c>/live.json</c> poll at once and asks for the stream again every 10 s; nothing of the
/// polling path is removed. The script is read, as 610-C, 965-C, 1092-C and 1093-C read it.
/// </summary>
public sealed class PageLiveStreamTests
{
    // PAGE-3 (1103-C item 1) · one stream per open page, named the way the page names the poll: with
    // the project the address chose, so a project's page is sent that project's slots alone.
    [Fact]
    public void Page_LiveStream_OneConnectionWithThePagesProject()
    {
        var script = PageScript.Html;
        Assert.Contains("new EventSource(streamUrl())", script, StringComparison.Ordinal);
        var url = script.IndexOf("function streamUrl() {", StringComparison.Ordinal);
        Assert.True(url >= 0, "the stream's address is built in one place");
        var body = script.Substring(url, script.IndexOf("\n  }", url, StringComparison.Ordinal) - url);
        Assert.Contains("'/live/stream'", body, StringComparison.Ordinal);
        Assert.Contains("?project=", body, StringComparison.Ordinal);
        Assert.Contains("location.search", body, StringComparison.Ordinal);

        // The card's 10 s: a stream that died is asked for again, not left closed for the session.
        Assert.Contains("AGAIN = 10000", script, StringComparison.Ordinal);
        Assert.Contains("streamAgain = Date.now() + AGAIN", script, StringComparison.Ordinal);
        Assert.Contains("if (!stream && Date.now() >= streamAgain) openStream();", script, StringComparison.Ordinal);
    }

    // PAGE-3 (1103-C item 2) · a frame updates the logs and the CPU text through the code the poll
    // already used: the frame's slot is drawn by applySlot, its load by the title bar's own element.
    [Fact]
    public void Page_LiveStream_FramesApplyLogsWithThePollsOwnCode()
    {
        var script = PageScript.Html;
        var apply = script.IndexOf("function applyFrame(j) {", StringComparison.Ordinal);
        Assert.True(apply >= 0, "one function takes a frame in");
        var body = script.Substring(apply, script.IndexOf("\n  }", apply, StringComparison.Ordinal) - apply);
        Assert.Contains("applyFrameSlot(s, slots[s]);", body, StringComparison.Ordinal);
        Assert.Contains("setLoad(j.load)", body, StringComparison.Ordinal);
        Assert.Contains("refresh(false)", body, StringComparison.Ordinal);

        // A frame carries only what changed, so a window grows from what it holds; a reset is the slot
        // moving to another run or card and keeps nothing of the one before (1093-C).
        var slot = script.IndexOf("function applyFrameSlot(s, v) {", StringComparison.Ordinal);
        Assert.True(slot >= 0, "a frame's slot is dealt with in one place");
        var slotBody = script.Substring(slot, script.IndexOf("\n  }", slot, StringComparison.Ordinal) - slot);
        Assert.Contains("if (v.reset) { h.run = ''; h.n = 0; h.lines = []; }", slotBody, StringComparison.Ordinal);
        Assert.Contains("applySlot(s, {", slotBody, StringComparison.Ordinal);
        Assert.Contains("next: h.n + got.length", slotBody, StringComparison.Ordinal);
    }

    // PAGE-3 (1103-C item 3) · the CPU and memory live in the title bar's own element, in the words the
    // server draws (PageRenderer.HostLoadText); what the frame does not carry - the core count - is
    // kept from what is on screen (1092-C's rule), and a page with no element is left alone.
    [Fact]
    public void Page_LiveStream_TheCpuTextFollowsTheFrames()
    {
        var script = PageScript.Html;
        var set = script.IndexOf("function setLoad(l) {", StringComparison.Ordinal);
        Assert.True(set >= 0, "the load text is written in one place");
        var body = script.Substring(set, script.IndexOf("\n  }", set, StringComparison.Ordinal) - set);
        Assert.Contains("getElementById('hostload')", body, StringComparison.Ordinal);
        Assert.Contains("if (!n) return;", body, StringComparison.Ordinal);
        Assert.Contains("'CPU ' + cpu + '%, '", body, StringComparison.Ordinal);
        Assert.Contains("G free of ", body, StringComparison.Ordinal);
        Assert.Contains("threads", body, StringComparison.Ordinal);

        // The server draws the same words into that element, so the two agree on the format.
        var load = new Scheduler.Core.Contracts.HostLoadSample
        {
            CpuPercent = 22,
            FreeMemoryBytes = 6L * 1024 * 1024 * 1024,
            TotalMemoryBytes = 31L * 1024 * 1024 * 1024,
            Cores = 32,
        };
        var html = new CombinedPageRenderer().Render([ViewsFixture.Full() with { HostLoad = load }], "", remote: false, controlsAllowed: true);
        Assert.Contains("<span id='hostload'>CPU 22%, 6G free of 31G, 32 threads</span>", html, StringComparison.Ordinal);
    }

    // PAGE-3 (1103-C item 4) · the stream falling over puts the page back on the poll: the error
    // handler closes it, the windows the frames had filled start over (the feed numbers its own lines
    // and runs), and the polling path is still there to take them.
    [Fact]
    public void Page_LiveStream_AnErrorFallsBackToThePoll()
    {
        var script = PageScript.Html;
        Assert.Contains("stream.onerror = streamDown;", script, StringComparison.Ordinal);
        var down = script.IndexOf("function streamDown() {", StringComparison.Ordinal);
        Assert.True(down >= 0, "one function takes the page off the stream");
        var body = script.Substring(down, script.IndexOf("\n  }", down, StringComparison.Ordinal) - down);
        Assert.Contains("stream.close()", body, StringComparison.Ordinal);
        Assert.Contains("stream = null", body, StringComparison.Ordinal);
        Assert.Contains("hist = {}; held = {}; streamSeq = -1;", body, StringComparison.Ordinal);
        Assert.Contains("schedule();", body, StringComparison.Ordinal);

        // While the stream is up the poll does not draw the windows - that is the frame's job - and it
        // does nothing else that would fight the frames; the moment the stream goes the poll draws them.
        Assert.Contains("if (!streaming) {", script, StringComparison.Ordinal);
        Assert.Contains("Object.keys(slots).forEach(function (s) { applySlot(s, slots[s]); });", script, StringComparison.Ordinal);
        Assert.Contains("get('/live.json' + sinceParam(), 4000)", script, StringComparison.Ordinal);
        var sched = script.IndexOf("function schedule() {", StringComparison.Ordinal);
        var schedBody = script.Substring(sched, script.IndexOf("\n  }", sched, StringComparison.Ordinal) - sched);
        Assert.Contains("Date.now() < lively ? INTERACT : waitFor ? FAST : EVERY", schedBody, StringComparison.Ordinal);
        Assert.Contains("if (streaming && !waitFor) ms = CONTROL;", schedBody, StringComparison.Ordinal);
    }

    // PAGE-3 (1103-C item 5) · both pages keep the script, and the pool's title bar carries the element
    // the frames move.
    [Fact]
    public void Page_LiveStream_ThePoolTitleBarHasTheLoadElement()
    {
        var snapshots = new[] { ViewsFixture.Full(), ViewsFixture.Plain() };
        var html = new CombinedPageRenderer().Render(snapshots, "", remote: false, controlsAllowed: true);
        Assert.Contains(PageScript.Html, html, StringComparison.Ordinal);
        Assert.Contains("<span id='hostload'>CPU ", html, StringComparison.Ordinal);
        Assert.Contains("threads</span>", html, StringComparison.Ordinal);
        Assert.Contains("id='live-state'", html, StringComparison.Ordinal);
    }
}
