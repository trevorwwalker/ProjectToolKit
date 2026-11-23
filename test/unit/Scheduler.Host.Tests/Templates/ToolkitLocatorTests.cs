using Scheduler.Host.Templates;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Templates;

/// <summary>Where ProjectToolkit is found (card 540-B): the setting first, else the folders above the exe.</summary>
public sealed class ToolkitLocatorTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Toolkit(string name)
    {
        var dir = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.Combine(dir, "assets", "templates"));
        return dir;
    }

    [Fact]
    public void The_nearest_folder_above_the_exe_with_the_templates_is_the_toolkit()
    {
        var toolkit = Toolkit("ProjectToolkit");
        var exe = Path.Combine(toolkit, "bin", "scheduler-next");
        Directory.CreateDirectory(exe);

        Assert.Equal(Path.GetFullPath(toolkit), ToolkitLocator.Find(null, _temp.Path, exe));
    }

    [Fact]
    public void The_toolkit_is_found_from_the_exe_published_to_out_windows_scheduler()
    {
        // The runnable layout (600-B, docs/conventions.md): the publish folder is out/windows/scheduler,
        // three folders under the toolkit, which is still the nearest one with assets/templates/.
        var toolkit = Toolkit("ProjectToolkit");
        var exe = Path.Combine(toolkit, "out", "windows", "scheduler");
        Directory.CreateDirectory(exe);
        File.WriteAllText(Path.Combine(exe, "scheduler-next.exe"), string.Empty);

        Assert.Equal(Path.GetFullPath(toolkit), ToolkitLocator.Find(null, _temp.Path, exe));
    }

    [Fact]
    public void A_publish_folder_with_no_templates_above_it_is_not_a_toolkit()
    {
        using var outside = TempDir.InSystemTemp(); // %TEMP% is above no toolkit; out/test-tmp is inside this one
        var exe = Path.Combine(outside.Path, "elsewhere", "out", "windows", "scheduler");
        Directory.CreateDirectory(exe);

        Assert.Null(ToolkitLocator.Find(null, outside.Path, exe));
    }

    [Fact]
    public void The_setting_wins_and_may_be_relative_to_the_repository()
    {
        var toolkit = Toolkit("kit");
        var other = Toolkit("other");
        var repo = Path.Combine(_temp.Path, "repo");
        Directory.CreateDirectory(repo);

        Assert.Equal(Path.GetFullPath(toolkit), ToolkitLocator.Find("../kit", repo, other));
        Assert.Equal(Path.GetFullPath(toolkit), ToolkitLocator.Find(toolkit, repo, null));
    }

    [Fact]
    public void No_toolkit_when_neither_has_the_templates()
    {
        var exe = Path.Combine(_temp.Path, "plain", "bin");
        Directory.CreateDirectory(exe);

        Assert.Null(ToolkitLocator.Find(Path.Combine(_temp.Path, "plain"), _temp.Path, null));
        Assert.Null(ToolkitLocator.Find(null, _temp.Path, null));
        Assert.Null(ToolkitLocator.Find(null, _temp.Path, exe) is { } found && found.StartsWith(_temp.Path, StringComparison.OrdinalIgnoreCase) ? found : null);
    }
}
