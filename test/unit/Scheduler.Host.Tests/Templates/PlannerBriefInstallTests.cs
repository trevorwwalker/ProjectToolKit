using Xunit;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// 1154-C: <c>bin/projecttoolkit.ps1 -Action new-project</c> copies the planner starter brief into
/// the new project as docs/planner-brief.md, the same way it copies the other layout files.
/// </summary>
public class PlannerBriefInstallTests
{
    [Fact]
    public void The_installer_copies_the_planner_brief_into_new_projects()
    {
        var script = File.ReadAllLines(Path.Combine(RepoRoot(), "bin", "projecttoolkit.ps1"));

        // Put 'docs/planner-brief.md' (Template 'docs/planner-brief.md')... — not $true: it becomes the project's own.
        Assert.Contains(script, l => l.Trim().StartsWith("Put 'docs/planner-brief.md'", StringComparison.Ordinal)
            && l.Contains("Template 'docs/planner-brief.md'", StringComparison.Ordinal));
        Assert.DoesNotContain(script, l => l.Trim().StartsWith("Put 'docs/planner-brief.md'", StringComparison.Ordinal)
            && l.TrimEnd().EndsWith("$true", StringComparison.Ordinal));
    }

    [Fact]
    public void The_brief_template_is_in_the_kit()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "assets", "templates", "docs", "planner-brief.md")));
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
