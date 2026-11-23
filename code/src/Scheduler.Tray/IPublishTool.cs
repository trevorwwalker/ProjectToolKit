namespace Scheduler.Tray;

/// <summary>
/// Makes the runnable folder beside the running one (967-B, BUILD-2): <c>dotnet publish</c> of the Host
/// project into <c>out\windows\scheduler.next</c>, which does not touch the exe that is running. Read
/// through an interface so an update can be tested without building anything.
/// </summary>
public interface IPublishTool
{
    /// <summary>
    /// Publish <paramref name="project"/> into <paramref name="outputDir"/>. True when the publish
    /// finished with the folder holding a runnable scheduler.
    /// </summary>
    Task<bool> PublishAsync(string project, string outputDir, CancellationToken cancel);
}
