// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Tests.Moves;

/// <summary>
/// CM-21 (card 790-A): the documents a worker reads tell it to ask the scheduler for every card move
/// with a request file, and never to move a card with git itself. The kit's README is the single
/// source and the project's copy is deployed from it, so the two stay identical.
/// Test plan: test-plans/claims-and-moves.md.
/// </summary>
public class CardMoveDocsTests
{
    private static readonly string[] WorkerDocs =
    [
        "assets/templates/jobs/README.md",
        "jobs/README.md",
        "docs/workflow.md",
    ];

    // CM-21: no worker-facing text sends it to move a card itself; all three name the request instead.
    [Fact]
    public void Worker_Docs_Ask_For_The_Move_Instead_Of_Making_It()
    {
        foreach (var doc in WorkerDocs)
        {
            var text = Read(doc);
            Assert.DoesNotContain("git mv", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("move request", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("only writer of card moves", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // CM-1 to CM-3: the kit README and its copy show the request file itself so a worker can write one.
    [Fact]
    public void Readme_Shows_The_Request_File_The_Worker_Writes()
    {
        foreach (var doc in new[] { "assets/templates/jobs/README.md", "jobs/README.md" })
        {
            var text = Read(doc);
            Assert.Contains("moves folder", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Card:", text, StringComparison.Ordinal);
            Assert.Contains("To:", text, StringComparison.Ordinal);
            Assert.Contains(".move", text, StringComparison.Ordinal);
        }
    }

    // The project's copy is the kit's text, byte for byte; it is deployed, not edited (the README's own rule).
    [Fact]
    public void Deployed_Readme_Still_Matches_The_Kit()
    {
        Assert.Equal(Read("assets/templates/jobs/README.md"), Read("jobs/README.md"));
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

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
