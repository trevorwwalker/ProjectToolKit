using System.Net;
using Scheduler.Core.Launch;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// WRK-14: a message typed on the page reaches the worker's msg file and its readable log, and an
/// idle slot gets nothing and says so. test-plans/workers.md; posted through the real server (050-C).
/// </summary>
public class WorkerMessageTests
{
    // WRK-14: the text is collapsed to one line and stamped [HH:mm], appended to msg-slotN.txt and
    // as "<< you: text" to the readable log, cut at 2000 characters; an idle slot writes nothing.
    [Fact]
    public async Task Workers_PageMessage_AppendedToFile()
    {
        using var harness = new PageActionHarness(project: "ptk-280b-send");
        harness.Running(1, card: "010-A-x");
        var msgFile = WorkerFiles.MessageFile(harness.LogDir, 1);
        var view = harness.WorkerLog(1);

        var first = await harness.Post("/send", $"slot=1&text={Uri.EscapeDataString("a\n\n  b")}");
        Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode);
        var written = Assert.Single(File.ReadAllLines(msgFile), line => line.Length > 0);
        Assert.Equal("[11:42] a b", written);
        Assert.Contains("<< you: a b", File.ReadAllText(view));
        Assert.Contains(harness.Log.Lines, line => line.Contains("message to slot 1 (010-A-x): a b"));

        await harness.Post("/send", $"slot=1&text={Uri.EscapeDataString(new string('x', 3000))}");
        var lines = File.ReadLines(msgFile).Where(line => line.Length > 0).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal($"[11:42] {new string('x', 2000)}", lines[1]);

        // A slot with no worker writes nothing, and the page is told why (MOVE-11).
        var idle = await harness.Post("/send", "slot=2&text=anyone%20there");
        Assert.Equal(HttpStatusCode.SeeOther, idle.StatusCode);
        Assert.False(File.Exists(WorkerFiles.MessageFile(harness.LogDir, 2)));
        Assert.Contains(harness.Notices, notice => notice.Contains("slot 2"));
        Assert.DoesNotContain(harness.Log.Lines, line => line.Contains("message to slot 2 ("));
        Assert.Equal(2, File.ReadAllLines(view).Count(line => line.StartsWith("<< you:", StringComparison.Ordinal)));
    }
}
