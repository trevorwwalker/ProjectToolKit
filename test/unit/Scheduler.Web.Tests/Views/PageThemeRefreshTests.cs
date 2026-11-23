using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 1027-C: a refresh replaces the top bar, and with it the theme menu, but the themes' style blocks are in the
/// head. A theme the server gained since the page was loaded was listed with no block and did nothing; the refresh
/// now takes the fetched page's blocks too. The script is read, not run, as in <see cref="PageLiveRefreshTests"/>.
/// </summary>
public sealed class PageThemeRefreshTests
{
    // 1027-C: the refresh syncs the head's theme blocks from the fetched page, keeps the theme on show, and the
    // pool page carries that script.
    [Fact]
    public void Refresh_TakesTheFetchedPagesThemeBlocks()
    {
        var script = PageScript.Html;

        var swap = script.IndexOf("['screen', 'desk'].forEach", StringComparison.Ordinal);
        Assert.True(swap > 0, "the refresh's swap of the top bar and desk is gone");
        Assert.True(script.IndexOf("syncThemeStyles(d);", swap, StringComparison.Ordinal) > swap, "the refresh does not sync the theme blocks");
        Assert.Contains("function syncThemeStyles(d)", script, StringComparison.Ordinal);
        Assert.Contains("d.querySelectorAll('style[data-theme]')", script, StringComparison.Ordinal);
        Assert.Contains("window.setTheme(on)", script, StringComparison.Ordinal);

        var html = new CombinedPageRenderer().Render([], "", remote: false, controlsAllowed: true);
        Assert.Contains("function syncThemeStyles(d)", html, StringComparison.Ordinal);
    }
}
