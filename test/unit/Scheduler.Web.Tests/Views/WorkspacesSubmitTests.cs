using Scheduler.Web.Views;
using Xunit;

namespace Scheduler.Web.Tests.Views;

/// <summary>1033-C: a Workspaces box changed again while its first save is in flight is posted too, never dropped.</summary>
public sealed class WorkspacesSubmitTests
{
    [Fact]
    public void SubmitHandler_LetsASecondWorkspacesPostThroughWhileTheFirstIsInFlight()
    {
        var html = PageScript.Html;
        var at = html.IndexOf("document.addEventListener('submit'", StringComparison.Ordinal);
        Assert.True(at > 0);
        var guard = html.IndexOf("if (submitting", at, StringComparison.Ordinal);
        Assert.True(guard > 0);
        var line = html[guard..html.IndexOf('\n', guard)];
        // The one-click-at-a-time guard must not apply to the workspaces form (8 -> 7 -> 6 by the arrows saved 7 and lost 6).
        Assert.Contains("/workspaces", line, StringComparison.Ordinal);
    }
}
