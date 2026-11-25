// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Host.Moves;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// Card 1010-C: the pool page's Open button and answer note reach the project the row belongs to.
/// The pool's actions hand a project-bound route to that project's own page actions (<c>For</c>),
/// so Open opens that project's file and an answer's note is written into that project's card.
/// </summary>
public class PoolCardActionTests
{
    private const string Jobs = "docs/jobs";
    private const string Path = Jobs + "/active/985-D-two-questions.md";
    private const string Text =
        "# 985-D: Two questions\n\nCategory: D\nStatus: needs-human: two questions\n\n## Questions\n\n" +
        "- Q1: Found: a. Question: keep it? [yes | no]\n\n## Result\n\n- found one thing\n";

    private static PoolPageActions Pool(params (string Name, PageActionHarness Harness)[] projects) =>
        new(new PoolPageActionParts
        {
            Projects = () => [.. projects.Select(p => (p.Name, (IPageActions)p.Harness.Actions))],
            Wake = () => { },
            ClearAgent = _ => true,
            WritePool = _ => true,
        });

    // 1010-C: Open on a pool row opens the card in the row's own project, not in another's.
    [Fact]
    public void Card1010_Open_OpensTheNamedProjectsFile()
    {
        var launcherA = new Launcher();
        var launcherB = new Launcher();
        using var a = new PageActionHarness(project: "alpha", launcher: launcherA);
        using var b = new PageActionHarness(project: "beta", launcher: launcherB);
        var card = Card();
        foreach (var harness in new[] { a, b })
        {
            var file = System.IO.Path.Combine(harness.Config.Repo, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Text);
            harness.Host.Cards.Add(card);
            harness.Tick_Once();
        }

        var result = Pool(("alpha", a), ("beta", b)).For("beta")!.Open(card.Name);

        Assert.True(result.Ok);
        Assert.Empty(launcherA.Opened);
        var opened = Assert.Single(launcherB.Opened);
        Assert.StartsWith(b.Config.Repo, opened, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("985-D-two-questions.md", opened, StringComparison.Ordinal);
    }

    // 1010-C: an answer with a note writes the note into the named project's card and no other's.
    [Fact]
    public void Card1010_Answer_WithNote_WritesTheNoteToTheNamedProjectsCard()
    {
        var mainA = new PageReviewRefreshTests.InMemoryMain();
        var mainB = new PageReviewRefreshTests.InMemoryMain();
        var committerA = new RecordingCommitter();
        var committerB = new RecordingCommitter();
        using var a = new PageActionHarness(project: "alpha", main: mainA, committer: committerA);
        using var b = new PageActionHarness(project: "beta", main: mainB, committer: committerB);
        foreach (var (harness, main) in new[] { (a, mainA), (b, mainB) })
        {
            main.Files[Path] = Text;
            harness.Host.Cards.Add(CardFiles.Parse(Jobs, Path, Text)!);
            harness.Tick_Once();
        }

        var result = Pool(("alpha", a), ("beta", b)).For("beta")!.Answer("985-D-two-questions", 1, "0", "use the second window");

        Assert.True(result.Ok);
        Assert.Empty(committerA.Changes);
        var change = Assert.Single(committerB.Changes);
        Assert.Contains(change.Writes.Values, text => text.Contains("use the second window", StringComparison.Ordinal));
    }

    private static Card Card() => new()
    {
        Folder = CardFolder.Active,
        Path = Path,
        Name = "985-D-two-questions",
        Num = 985,
        Category = "D",
        Id = "985-D",
        Title = "Two questions",
        Status = "ready",
    };

    private sealed class Launcher : IFileLauncher
    {
        public List<string> Opened { get; } = [];

        public void Open(string file) => Opened.Add(file);
    }
}
