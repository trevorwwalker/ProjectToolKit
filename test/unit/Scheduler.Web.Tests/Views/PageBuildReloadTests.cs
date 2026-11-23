using System.Text.Json;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

// 1109-C: an open page of an older build reloads itself in full when the feed names another build.
public class PageBuildReloadTests
{
    [Fact]
    public void Pages_CarryTheirBuildInAMetaTag()
    {
        var snapshot = ViewsFixture.Full() with { Version = "3.0.116" };

        var single = new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);
        var combined = new CombinedPageRenderer().Render(new PoolPageSnapshot { Built = snapshot.Built, Pages = [snapshot] }, remote: false, controlsAllowed: true);

        Assert.Contains("<meta name='build-version' content='3.0.116'>", single, StringComparison.Ordinal);
        Assert.Contains("<meta name='build-version' content='3.0.116'>", combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJson_CarriesTheRunningBuild()
    {
        using var web = new WebHarness();
        web.Data.Latest = ViewsFixture.Full() with { Version = "3.0.117" };

        var response = await web.Client.GetAsync("/live.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("3.0.117", doc.RootElement.GetProperty("build").GetString());
    }

    [Fact]
    public void Script_ReloadsInFullWhenTheFeedNamesAnotherBuild_BothFromThePollAndTheFrames()
    {
        var script = PageScript.Html;

        Assert.Contains("meta[name=build-version]", script, StringComparison.Ordinal);
        Assert.Contains("if (typeof b === 'string' && b && buildMine && b !== buildMine) buildNew = b;", script, StringComparison.Ordinal);
        Assert.Contains("location.reload();", script, StringComparison.Ordinal);
        Assert.Contains("checkBuild(j.build);", script, StringComparison.Ordinal);
        Assert.Equal(2, script.Split("checkBuild(j.build);").Length - 1);
    }

    [Fact]
    public void Script_HoldsTheReloadBackWhileTheUserIsBusy_AndTriesAgain()
    {
        var script = PageScript.Html;

        Assert.Contains("if (submitting || userBusy()) {", script, StringComparison.Ordinal);
        Assert.Contains("setTimeout(function () { buildWait = false; checkBuild(); }, 1000);", script, StringComparison.Ordinal);
        // The guard comes before the reload.
        Assert.True(script.IndexOf("if (submitting || userBusy()) {", StringComparison.Ordinal)
            < script.IndexOf("saveNotes();", script.IndexOf("function checkBuild", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public void Script_KeepsNotesAcrossTheReload_KeyedByProjectAndCard()
    {
        var script = PageScript.Html;

        Assert.Contains("if (i.name === 'note' && i.value) sessionStorage.setItem(keyOf(i), i.value);", script, StringComparison.Ordinal);
        Assert.Contains("var k = keyOf(i), v = sessionStorage.getItem(k);", script, StringComparison.Ordinal);
        Assert.Contains("restoreNotes();", script, StringComparison.Ordinal);
        Assert.Contains("if (i.name === 'note') return noteKey(", script, StringComparison.Ordinal);
    }
}
