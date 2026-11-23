using Scheduler.Core.WebContract;
using Scheduler.Web.Server;

namespace Scheduler.Web.Tests.Server.Fakes;

/// <summary>
/// A stand-in for the views (090-C). It says which version it was asked for, so a test can see
/// whether the server asked for the local page or the read-only remote one (PAGE-7).
/// </summary>
internal sealed class FakePageRenderer : IPageRenderer
{
    /// <summary>The HTML to answer with.</summary>
    public string Html { get; set; } = "<html>the page</html>";

    /// <summary>The versions that were rendered, in order.</summary>
    public List<(bool Remote, bool ControlsAllowed)> Rendered { get; } = [];

    /// <summary>The last version asked for.</summary>
    public (bool Remote, bool ControlsAllowed)? Last { get; private set; }

    public string Render(PageSnapshot snapshot, bool remote, bool controlsAllowed)
    {
        Last = (remote, controlsAllowed);
        Rendered.Add((remote, controlsAllowed));
        return controlsAllowed ? Html : $"{Html} (Only on the PC running the scheduler.)";
    }
}
