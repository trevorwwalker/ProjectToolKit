namespace Scheduler.Host.Moves;

/// <summary>
/// Opens a file through the shell, as <c>Start-Process -FilePath</c> does (Open-Path L1557), then
/// brings the window showing it to the front (Raise-Editor L1575, 986-B) on a background task, so the
/// page's action answers at once and the raise gives up after <see cref="WindowRaiser.DefaultLimit"/>.
/// </summary>
/// <param name="api">The window calls; Windows' own unless a test fakes them.</param>
/// <param name="background">Runs the raise; a thread-pool task unless a test runs it itself.</param>
public sealed class ShellLauncher(IWindowApi? api = null, Action<Action>? background = null) : IFileLauncher
{
    private readonly IWindowApi windows = api ?? new Win32WindowApi();
    private readonly Action<Action> run = background ?? (a => Task.Run(a));

    /// <inheritdoc/>
    public void Open(string file)
    {
        var pid = windows.Start(file);
        var raiser = new WindowRaiser(windows, WindowRaiser.DefaultLimit, WindowRaiser.DefaultStep);
        run(() => raiser.Raise(file, pid));
    }
}
