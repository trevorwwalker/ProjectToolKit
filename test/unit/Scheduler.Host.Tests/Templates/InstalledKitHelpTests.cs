// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Xunit;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// 1248-D (INS-16, docs/design/tdd/installer.md): the help text of <c>bin/projecttoolkit.ps1</c> assumes a clone
/// when it tells the reader to <c>dotnet publish</c> and start the tray. An installed kit has no <c>code\src</c>
/// and its installer put a Switchyard shortcut in the Start menu, so the help says to start Switchyard from the
/// Start menu there.
/// </summary>
public class InstalledKitHelpTests
{
    /// <summary>The script's comment-based help (the &lt;# ... #&gt; block before param()).</summary>
    private static string Help
    {
        get
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "bin", "projecttoolkit.ps1"));
            var start = text.IndexOf("<#", StringComparison.Ordinal);
            var end = text.IndexOf("#>", StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "bin/projecttoolkit.ps1 has no comment-based help block");
            return text.Substring(start, end - start);
        }
    }

    [Fact]
    public void The_publish_steps_are_offered_as_the_clone_path()
    {
        // The dotnet publish wording stays, but as what to do in a clone, not as the only way.
        Assert.Matches(@"In a clone of the kit[^\r\n]*\r?\n\s*`dotnet publish", Help);
    }

    [Fact]
    public void An_installed_kit_is_told_to_start_switchyard_from_the_start_menu()
    {
        var flat = Help.Replace("\r\n", "\n").Replace('\n', ' ');
        Assert.Contains("installed kit", flat, StringComparison.Ordinal);
        Assert.Contains("no code\\src", flat, StringComparison.Ordinal);
        Assert.Matches("start Switchyard from the Start menu", flat);
    }

    [Fact]
    public void The_help_still_names_the_exe_the_firewall_rule_is_bound_to()
    {
        Assert.Contains("out\\windows\\scheduler\\scheduler-tray.exe", Help, StringComparison.Ordinal);
    }

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
