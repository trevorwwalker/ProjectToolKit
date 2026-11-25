// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Xunit;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// 1270-D: <c>docs/ai/</c> was removed as the place for new plans on 2026-11-23 (plans start in
/// <c>jobs/plans/</c> as <c>Status: draft</c>), but the layout trees drawn in the kit's templates and in
/// this repository's own docs still showed an <c>ai/</c> line under <c>docs/</c>. The two templates are
/// copied into every new project, so a fresh project's README and conventions described a folder that
/// nothing creates. This test keeps the four trees honest.
/// </summary>
public class NoDocsAiInLayoutTests
{
    private static readonly string[] LayoutFiles =
    [
        Path.Combine("assets", "templates", "conventions.md"),
        Path.Combine("assets", "templates", "readme.md"),
        Path.Combine("docs", "conventions.md"),
        Path.Combine("docs", "workflow.md"),
    ];

    // A tree line whose entry is the ai/ folder: optional indent, optional box drawing, then "ai/ ".
    private static readonly Regex AiTreeLine = new(@"^\s*(│\s*)?(├──|└──)?\s*ai/\s", RegexOptions.Compiled);

    [Fact]
    public void No_layout_tree_lists_an_ai_folder()
    {
        var root = RepoRoot();
        foreach (var relative in LayoutFiles)
        {
            var path = Path.Combine(root, relative);
            Assert.True(File.Exists(path), $"{relative} is missing from the kit");
            foreach (var (line, text) in File.ReadAllLines(path).Select((t, i) => (i + 1, t)))
            {
                Assert.False(AiTreeLine.IsMatch(text), $"{relative}:{line} draws an ai/ folder in the layout tree: {text}");
                Assert.DoesNotContain("docs/ai", text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>The kit repository root, found by walking up from the test's output folder.</summary>
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
