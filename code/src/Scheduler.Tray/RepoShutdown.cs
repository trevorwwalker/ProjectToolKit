using System.Security.Cryptography;
using System.Text;

namespace Scheduler.Tray;

/// <summary>
/// The shutdown event every scheduler listens on for its repository (975-B), beside the one a tray that
/// started it hands it with <c>-ShutdownEvent</c>. A tray started while this scheduler is running finds it
/// by name - its being there is how the tray knows a scheduler for the repository is up - and sets it
/// for Restart and Exit, so a tray that did not start the scheduler can still end it cleanly (670-B).
/// The name is keyed by the repository's full path, so two repositories' schedulers never share one. The
/// scheduler computes the same name (Scheduler.Host's <c>RepoShutdown</c>); the two must stay the same.
/// </summary>
public static class RepoShutdown
{
    /// <summary>The event's name for the repository at <paramref name="repo"/>.</summary>
    public static string Name(string repo)
    {
        var full = Path.GetFullPath(repo).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
        return $"ProjectToolkit-scheduler-{hash}-shutdown";
    }
}
