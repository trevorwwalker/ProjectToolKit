using Scheduler.Core.Categories;
using Scheduler.Core.WebContract;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// Card 992-B: the Categories panel's Save, with Worker types Save's safety (947-B) so a settings change
/// never cascades: it writes and commits the categories file through the jobs committer; a changed
/// letter or a removed category that open cards use is refused with the cards named (a card's category
/// is its file name's letter, which a header rewrite cannot change: 992-B Q1); and a glob the scope rules cannot use is refused.
/// </summary>
public class CategoriesSaveTests
{
    private const string Jobs = "docs/jobs";
    private const string CategoriesRel = Jobs + "/categories.json";

    private const string Categories = """
        {
          "about": "The categories of this project.",
          "lanes": {
            "A": {"name": "core", "purpose": "logic", "owns": ["src/a/"], "except": [], "alsoOwns": "kept"},
            "B": {"name": "host", "purpose": "machine", "owns": ["src/b/"], "except": []}
          },
          "shared": {"about": "add only", "addOnly": [".gitignore"], "appendOnly": []}
        }
        """;

    private static string CardOf(string title, string headerLine, string body = "Do the thing.\n") =>
        $"# {title}\n\n{headerLine}Status: ready\nPriority: normal\n\n## Task\n\n{body}";

    private static PageCategory Row(string loaded, string now, string name, params string[] owns) =>
        new() { Letter = now, OriginalLetter = loaded, Name = name, Purpose = "p", Owns = owns };

    private static (PageActionHarness Harness, RecordingCommitter Committer, PageReviewRefreshTests.InMemoryMain Main) Make(string project)
    {
        var main = new PageReviewRefreshTests.InMemoryMain();
        main.Files[CategoriesRel] = Categories;
        var committer = new RecordingCommitter();
        var harness = new PageActionHarness(categoriesJson: Categories, main: main, committer: committer, project: project);
        harness.Tick_Once();
        return (harness, committer, main);
    }

    /// <summary>992-B: Save writes the panel's categories and shared list and commits the file; what the panel does not show is kept.</summary>
    [Fact]
    public void Categories_Save_WritesAndCommitsTheFile()
    {
        var (harness, committer, _) = Make("ptk-992b-save");
        using var _h = harness;

        var saved = harness.Actions.SaveCategories(
            [Row("A", "A", "core", "src/a/", "src/a2/"), Row("B", "B", "host", "src/b/"), Row("", "C", "web", "src/c/**/*.cs")],
            [".gitignore", "docs/known_gaps.md"]);

        Assert.True(saved.Ok, saved.Error);
        var change = Assert.Single(committer.Changes);
        Assert.StartsWith("scheduler: categories - A core, B host, C web", change.Message, StringComparison.Ordinal);
        var text = Assert.Single(change.Writes).Value;
        var file = CategoriesEdit.Parse(text);
        Assert.NotNull(file);
        Assert.Equal(["A", "B", "C"], file.Categories.Keys);
        Assert.Equal(["src/a/", "src/a2/"], file.Categories["A"].Owns);
        Assert.Equal(["src/c/**/*.cs"], file.Categories["C"].Owns);
        Assert.Equal([".gitignore", "docs/known_gaps.md"], file.AddOnly);
        Assert.Equal("kept", file.Categories["A"].AlsoOwns);
        Assert.Contains("\"about\": \"The categories of this project.\"", text, StringComparison.Ordinal);
        Assert.Contains("\"appendOnly\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 992-B Q1 (developer, 2026-11-20): dispatch reads a card's category from its file name, which a
    /// header rewrite cannot change, so changing a letter that open cards use is refused, naming the
    /// cards (by file name or Category:/Lane: header), and nothing is committed; done cards do not count.
    /// </summary>
    [Fact]
    public void Categories_LetterChange_UsedByOpenCards_RefusedWithTheCards()
    {
        var (harness, committer, main) = Make("ptk-992b-letter");
        using var _h = harness;
        main.Files[$"{Jobs}/queue/961-B-one.md"] = CardOf("961-B: One", "Category: B\n");
        main.Files[$"{Jobs}/active/962-A-two.md"] = CardOf("962-A: Two", "Lane: B\n");
        main.Files[$"{Jobs}/queue/963-A-other.md"] = CardOf("963-A: Other", "Category: A\n");
        main.Files[$"{Jobs}/done/960-B-old.md"] = CardOf("960-B: Old", "Category: B\n");

        var saved = harness.Actions.SaveCategories([Row("A", "A", "core", "src/a/"), Row("B", "H", "host", "src/b/")], [".gitignore"]);

        Assert.False(saved.Ok);
        Assert.Contains("category B is used by 961-B, 962-A", saved.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("963-A", saved.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("960-B", saved.Error, StringComparison.Ordinal);
        Assert.Empty(committer.Changes);
    }

    /// <summary>992-B: a letter no open card uses can change; only the categories file is committed, and done cards are left alone.</summary>
    [Fact]
    public void Categories_LetterChange_Unused_SavedWithoutTouchingCards()
    {
        var (harness, committer, main) = Make("ptk-992b-letter-free");
        using var _h = harness;
        main.Files[$"{Jobs}/queue/963-A-other.md"] = CardOf("963-A: Other", "Category: A\n");
        main.Files[$"{Jobs}/done/960-B-old.md"] = CardOf("960-B: Old", "Category: B\n");

        var saved = harness.Actions.SaveCategories([Row("A", "A", "core", "src/a/"), Row("B", "H", "host", "src/b/")], [".gitignore"]);

        Assert.True(saved.Ok, saved.Error);
        var change = Assert.Single(committer.Changes);
        Assert.Contains("letter B to H", change.Message, StringComparison.Ordinal);
        var write = Assert.Single(change.Writes);
        Assert.Equal(CategoriesRel, write.Key);
        Assert.Equal(["A", "H"], CategoriesEdit.Parse(write.Value)!.Categories.Keys);
    }

    /// <summary>992-B: removing a category that open cards use is refused, naming the cards, and nothing is committed.</summary>
    [Fact]
    public void Categories_RemoveUsed_RefusedWithTheCards()
    {
        var (harness, committer, main) = Make("ptk-992b-remove");
        using var _h = harness;
        main.Files[$"{Jobs}/queue/971-B-one.md"] = CardOf("971-B: One", "Category: B\n");
        main.Files[$"{Jobs}/review/972-A-two.md"] = CardOf("972-A: Two", "Category: B\n");

        var saved = harness.Actions.SaveCategories([Row("A", "A", "core", "src/a/")], [".gitignore"]);

        Assert.False(saved.Ok);
        Assert.Contains("category B is used by 971-B, 972-A", saved.Error, StringComparison.Ordinal);
        Assert.Empty(committer.Changes);
        Assert.Contains(harness.Notices, n => n.Contains("971-B", StringComparison.Ordinal));
    }

    /// <summary>992-B: a category no open card uses can be removed.</summary>
    [Fact]
    public void Categories_RemoveUnused_Saved()
    {
        var (harness, committer, main) = Make("ptk-992b-unused");
        using var _h = harness;
        main.Files[$"{Jobs}/done/970-B-old.md"] = CardOf("970-B: Old", "Category: B\n");

        var saved = harness.Actions.SaveCategories([Row("A", "A", "core", "src/a/")], [".gitignore"]);

        Assert.True(saved.Ok, saved.Error);
        Assert.Equal(["A"], CategoriesEdit.Parse(Assert.Single(committer.Changes).Writes[CategoriesRel])!.Categories.Keys);
    }

    /// <summary>992-B, 961-A: a glob the scope rules cannot match is refused with a message; so are a bad and a repeated letter.</summary>
    [Fact]
    public void Categories_InvalidGlobOrLetter_RefusedWithAMessage()
    {
        var (harness, committer, _) = Make("ptk-992b-invalid");
        using var _h = harness;

        var glob = harness.Actions.SaveCategories([Row("A", "A", "core", "src/[ab]/*.cs"), Row("B", "B", "host", "src/b/")], []);
        var letter = harness.Actions.SaveCategories([Row("A", "AB", "core", "src/a/"), Row("B", "B", "host", "src/b/")], []);
        var twice = harness.Actions.SaveCategories([Row("A", "A", "core", "src/a/"), Row("B", "a", "host", "src/b/")], []);

        Assert.False(glob.Ok);
        Assert.Contains("category A: src/[ab]/*.cs has a pattern that cannot be matched", glob.Error, StringComparison.Ordinal);
        Assert.False(letter.Ok);
        Assert.Contains("\"AB\" is not a category letter", letter.Error, StringComparison.Ordinal);
        Assert.False(twice.Ok);
        Assert.Contains("letter A is used by more than one category", twice.Error, StringComparison.Ordinal);
        Assert.Empty(committer.Changes);
    }

    /// <summary>992-B, 977-A: overlapping categories are saved, with a warning on the page.</summary>
    [Fact]
    public void Categories_Overlap_SavedWithAWarning()
    {
        var (harness, committer, _) = Make("ptk-992b-overlap");
        using var _h = harness;

        var saved = harness.Actions.SaveCategories([Row("A", "A", "core", "src/"), Row("B", "B", "host", "src/b/")], []);

        Assert.True(saved.Ok, saved.Error);
        Assert.Single(committer.Changes);
        Assert.Contains(harness.Notices, n => n.StartsWith("categories saved, but they overlap: A owns src/", StringComparison.Ordinal));
    }
}
