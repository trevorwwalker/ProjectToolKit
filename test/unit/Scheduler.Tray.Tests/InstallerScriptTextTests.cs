// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 1272-B, INS-19: ProjectToolkit.iss logs the stop-for-update exe, exit code and line, and the finish
/// page names the guide's script by its installed full path (the PATH task is unticked by default, INS-9).
/// </summary>
public class InstallerScriptTextTests
{
    [Fact]
    public void The_stop_for_update_is_logged()
    {
        var text = ReadIss();

        Assert.Contains("Log('stop for update: '", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_finish_page_names_the_script_by_its_full_path()
    {
        var text = ReadIss();

        Assert.Contains(@"{app}\bin\projecttoolkit.ps1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("'(projecttoolkit.ps1 -Action new-project)", text, StringComparison.Ordinal);
    }

    private static string ReadIss()
    {
        return File.ReadAllText(Path.Combine(RepoRoot(), "code", "src", "Installer", "ProjectToolkit.iss"));
    }

    // Same lookup as StopForUpdateTests finds bin/test-installer.ps1.
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "bin", "projecttoolkit.ps1")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("bin/projecttoolkit.ps1 not found above the test's folder");
    }
}
