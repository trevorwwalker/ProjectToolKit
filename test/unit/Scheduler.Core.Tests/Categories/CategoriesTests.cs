// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.Categories;

/// <summary>
/// categories.json from main: reading, held categories, overlaps and the brief (LANE-1, LANE-2, LANE-4,
/// LANE-5; <c>Read-Categories</c> L1622-1645, <c>Category-Overlaps</c> L1659, <c>Category-Brief</c> L1680).
/// Test plan: scheduler-next/test-plans/dispatch.md.
/// </summary>
public class CategoriesTests
{
    private const string Rel = "docs/jobs/lanes.json";

    private sealed class FakeLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }

    private sealed class FakeFiles : IStateFiles
    {
        public Dictionary<StateFile, string> Texts { get; } = [];

        public string? Read(StateFile file) => Texts.GetValueOrDefault(file);

        public void Write(StateFile file, string text) => Texts[file] = text;
    }

    private static Card CardOf(string id, CardFolder folder) => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder}/{id}.md".ToLowerInvariant(),
        Name = id,
        Num = long.Parse(id[..id.IndexOf('-')], System.Globalization.CultureInfo.InvariantCulture), // 931-A: any width
        Category = id[4..5],
        Id = id[..5],
    };

    private const string GoodCategories = """
        {
          "lanes": {
            "A": { "name": "core", "owns": ["src/core/", "a.txt"], "except": ["src/core/x/"], "alsoOwns": "the docs" },
            "B": { "name": "host", "owns": ["src/host/"] }
          },
          "shared": { "addOnly": [".gitignore", "docs/gaps.md"], "rerecord": ["snap.txt"], "rerecordRule": "only your own" }
        }
        """;

    private static CategoriesFile ParseOwns(params (string Letter, string[] Owns, string[] Except)[] categories)
    {
        var map = new Dictionary<string, CategoryDefinition>(Text.Comparer);
        foreach (var (letter, owns, except) in categories)
        {
            map[letter] = new CategoryDefinition { Letter = letter, Owns = owns, Except = except };
        }

        return new CategoriesFile { Categories = map };
    }

    // The same, with categories.json's shared add-only list (SC-5).
    private static CategoriesFile ParseOwnsAddOnly(
        string[] addOnly,
        params (string Letter, string[] Owns, string[] Except)[] categories)
    {
        var map = new Dictionary<string, CategoryDefinition>(Text.Comparer);
        foreach (var (letter, owns, except) in categories)
        {
            map[letter] = new CategoryDefinition { Letter = letter, Owns = owns, Except = except };
        }

        return new CategoriesFile { Categories = map, AddOnly = addOnly };
    }

    // LANE-1: every field of categories.json is read.
    [Fact]
    public void Categories_ReadFromMain()
    {
        var registry = new CategoryRegistry(new FakeLog(), new FakeFiles(), Rel);
        registry.Read(GoodCategories, []);

        var file = registry.Current!;
        Assert.Equal(["A", "B"], file.Categories.Keys.ToArray());
        var a = file.Categories["a"];
        Assert.Equal("core", a.Name);
        Assert.Equal(["src/core/", "a.txt"], a.Owns);
        Assert.Equal(["src/core/x/"], a.Except);
        Assert.Equal("the docs", a.AlsoOwns);
        Assert.Equal([], file.Categories["B"].Except);
        Assert.Equal([".gitignore", "docs/gaps.md"], file.AddOnly);
        Assert.Equal(["snap.txt"], file.Rerecord);
        Assert.Equal("only your own", file.RerecordRule);
    }

    // LANE-1: invalid JSON is logged and the previous categories stay; no categories key is ignored, unlogged.
    [Fact]
    public void Categories_InvalidJsonOrNoCategoriesKey_PreviousKept()
    {
        var log = new FakeLog();
        var registry = new CategoryRegistry(log, new FakeFiles(), Rel);
        registry.Read(GoodCategories, []);
        var before = registry.Current;

        registry.Read("{ not json", []);
        Assert.Same(before, registry.Current);
        Assert.Equal(2, log.Lines.Count);
        Assert.Contains("not valid JSON", log.Lines[1]);

        registry.Read("{}", []);
        Assert.Same(before, registry.Current);
        Assert.Equal(3, log.Lines.Count);
        Assert.Contains("ignored", log.Lines[2]);
    }

    private const string CategoriesKeyText = """
        { "categories": { "A": { "name": "core", "owns": ["src/"] } }, "shared": { "addOnly": ["x"] } }
        """;

    private const string LanesKeyText = """
        { "lanes": { "A": { "name": "core", "owns": ["src/"] } }, "shared": { "addOnly": ["x"] } }
        """;

    // 1486-A: "categories" is the key, "lanes" the old name read the same, "categories" wins, neither is ignored.
    [Fact]
    public void Parse_CategoriesKey_LanesKey_Both_Neither()
    {
        var a = CategoriesEdit.Parse(CategoriesKeyText);
        var b = CategoriesEdit.Parse(LanesKeyText);
        Assert.Equal(a!.Categories["A"].Json, b!.Categories["A"].Json);
        Assert.Equal(["x"], b.AddOnly);

        var both = CategoriesEdit.Parse(
            """{ "lanes": { "A": { "name": "old" } }, "categories": { "A": { "name": "new" } } }""");
        Assert.Equal("new", both!.Categories["A"].Name);

        Assert.Null(CategoriesEdit.Parse("{}"));
    }

    // 1486-A: a page save of a "lanes" file writes "categories" in the same place, no "lanes", the same definitions.
    [Fact]
    public void Edit_SaveOfLanesFile_WritesCategoriesInPlace()
    {
        const string main = """
            { "note": 1, "lanes": { "A": { "name": "core", "owns": ["src/"], "except": [] } }, "shared": { "addOnly": ["x"] }, "tail": true }
            """;
        var rows = new[] { new PageCategory { Letter = "A", Name = "core", Owns = ["src/"], Except = [], OriginalLetter = "A" } };
        var text = CategoriesEdit.Text(main, rows, ["x"]);

        Assert.DoesNotContain("\"lanes\"", text);
        var order = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject().Select(kv => kv.Key).ToArray();
        Assert.Equal(["note", "categories", "shared", "tail"], order);
        Assert.Equal(
            CategoriesEdit.Parse(main)!.Categories["A"].Json,
            CategoriesEdit.Parse(text)!.Categories["A"].Json);
    }

    // 1486-A: the log line, for each of the four cases, once and again only when it changes.
    [Fact]
    public void Registry_LogsWhatWasRead_OnceAndOnChange()
    {
        var log = new FakeLog();
        var registry = new CategoryRegistry(log, new FakeFiles(), Rel);
        registry.Read(null, []);
        registry.Read(null, []);
        registry.Read(CategoriesKeyText, []);
        registry.Read(CategoriesKeyText, []);
        registry.Read(LanesKeyText, []);
        registry.Read("{}", []);
        registry.Read("{}", []);
        Assert.Equal(
            [
                $"categories: no {Rel} on main",
                $"categories: read \"categories\" from {Rel} (1 categories)",
                $"categories: read \"lanes\" (the old name) from {Rel} (1 categories)",
                $"categories: {Rel} ignored: it has no \"categories\" section",
            ],
            log.Lines);
    }

    // LANE-2 as changed by SC-4 (320-A Q5, 560-A): a changed category with cards pending is not held;
    // the new definition is accepted at once, logged once, and its cards take the new scope.
    [Fact]
    public void Categories_ChangedWithPendingCards_AcceptedNotHeld()
    {
        var log = new FakeLog();
        var files = new FakeFiles();
        var registry = new CategoryRegistry(log, files, Rel);
        registry.Read(GoodCategories, []);

        var cards = new List<Card>();
        for (var i = 1; i <= 8; i++)
        {
            cards.Add(CardOf($"{i * 10:000}-A-card", i % 2 == 0 ? CardFolder.Active : CardFolder.Queue));
        }

        cards.Add(CardOf("900-A-done", CardFolder.Done));
        cards.Add(CardOf("910-B-other", CardFolder.Queue));

        var changed = GoodCategories.Replace("src/core/\"", "src/core2/\"");
        registry.Read(changed, cards);
        registry.Read(changed, cards);
        registry.Read(changed, cards);

        Assert.Single(log.Lines, l => l.Contains("category A redefined"));
        Assert.DoesNotContain(log.Lines, l => l.Contains("held"));
        Assert.Contains("src/core2/", files.Read(StateFile.CategoriesAccepted));
    }

    // LANE-2: the same category re-indented is compared as compact JSON, so it is not a change.
    [Fact]
    public void Categories_SameDefinitionDifferentFormatting_NotAChange()
    {
        var log = new FakeLog();
        var registry = new CategoryRegistry(log, new FakeFiles(), Rel);
        registry.Read(GoodCategories, []);

        var compact = """{"lanes":{"A":{"name":"core","owns":["src/core/","a.txt"],"except":["src/core/x/"],"alsoOwns":"the docs"},"B":{"name":"host","owns":["src/host/"]}},"shared":{}}""";
        var cards = new[] { CardOf("010-A-card", CardFolder.Queue), CardOf("020-B-card", CardFolder.Active) };
        registry.Read(compact, cards);

        Assert.Single(log.Lines); // only 1486-A's "categories: read" line, no redefinition
        Assert.StartsWith("categories: read", log.Lines[0], StringComparison.Ordinal);
    }

    // LANE-4: same entry owned twice; a folder holding another category's path; an except clears it.
    [Fact]
    public void Categories_Overlaps_Warnings()
    {
        string[] none = [];

        var same = CategoryOverlaps.Find(ParseOwns(("A", ["src/x.cs"], none), ("B", ["src/x.cs"], none)));
        Assert.Equal(["src/x.cs is owned by both A and B"], same);

        var folder = CategoryOverlaps.Find(ParseOwns(("A", ["src/"], none), ("B", ["src/a/*"], none)));
        Assert.Equal(["A owns src/, which holds B's src/a/* (add it to A's except)"], folder);

        var excepted = CategoryOverlaps.Find(ParseOwns(("A", ["src/"], ["src/a/"]), ("B", ["src/a/*"], none)));
        Assert.Empty(excepted);
    }

    // LANE-4 as updated on 977-A (2026-11-19), DISP-25, SC-8: the warning uses the scope overlap test
    // (can one path match both), so globs are seen: the glob/file and folder/glob pairs warn, disjoint
    // patterns and `src/*` against the folder `src/x/` do not, and an except or an add-only path clears it.
    [Fact]
    public void Categories_Overlaps_GlobOwnsWarn()
    {
        string[] none = [];

        var globAndFile = CategoryOverlaps.Find(ParseOwns(("A", ["vm/68000.*"], none), ("B", ["vm/68000.cpp"], none)));
        Assert.Equal(["A owns vm/68000.*, which holds B's vm/68000.cpp (add it to A's except)"], globAndFile);

        var folderAndGlob = CategoryOverlaps.Find(ParseOwns(("A", ["vm/"], none), ("B", ["vm/*.h"], none)));
        Assert.Equal(["A owns vm/, which holds B's vm/*.h (add it to A's except)"], folderAndGlob);

        var twoGlobs = CategoryOverlaps.Find(ParseOwns(("A", ["src/a*"], none), ("B", ["src/*b"], none)));
        Assert.Equal(["A's src/a* and B's src/*b can match the same path (give one of them an except)"], twoGlobs);

        // A `**` does cross `/`, where a single `*` cannot (DISP-25).
        Assert.NotEmpty(CategoryOverlaps.Find(ParseOwns(("A", ["src/**"], none), ("B", ["src/x/"], none))));
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(("A", ["src/*"], none), ("B", ["src/x/"], none))));
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(("A", ["vm/*.h"], none), ("B", ["vm/*.cpp"], none))));

        // The except the warning asks for clears it: A keeps the file B owns out of its own glob.
        var excepted = CategoryOverlaps.Find(ParseOwns(("A", ["vm/68000.*"], ["vm/68000.cpp"]), ("B", ["vm/68000.cpp"], none)));
        Assert.Empty(excepted);
    }

    // LANE-4 as the developer decided it for 977-A Q1 (2026-11-19), DISP-25: an `except` entry leaves a
    // pair out only when it can be shown to cover one side of it, exactly as the dispatch check requires.
    // A glob `except` that could name the disputed path but cannot be shown to cover it keeps the warning.
    [Fact]
    public void Categories_Overlaps_ExceptMustCoverToSilence()
    {
        string[] none = [];

        // `vm/*` could name vm/*.h, but neither side is shown to be inside it, so the pair still warns.
        var cannotCover = CategoryOverlaps.Find(ParseOwns(("A", ["vm/"], none), ("B", ["vm/*.h"], ["vm/*"])));
        Assert.Equal(["A owns vm/, which holds B's vm/*.h (add it to A's except)"], cannotCover);

        // The except that does cover one side clears it, from either category, glob or plain path.
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(("A", ["vm/"], none), ("B", ["vm/*.h"], ["vm/*.h"]))));
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(("A", ["vm/"], ["vm/*.h"]), ("B", ["vm/*.h"], none))));
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(("A", ["src/"], ["src/a/"]), ("B", ["src/a/*"], none))));

        // A glob `except` covers the concrete file the other category owns, and the equal glob entry, which is
        // how the AmigaX categories keep `platform/null/` (C) and `platform/null/unittest.*` (A) quiet.
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(
            ("A", ["platform/null/"], ["platform/null/unittest.*"]), ("B", ["platform/null/unittest.cpp"], none))));
        Assert.Empty(CategoryOverlaps.Find(ParseOwns(
            ("A", ["platform/null/"], ["platform/null/unittest.*"]), ("B", ["platform/null/unittest.*"], none))));
    }

    // LANE-4, SC-5: two categories both naming a shared add-only path is allowed, as it is between scopes.
    [Fact]
    public void Categories_Overlaps_AddOnlyOwnsDoNotWarn()
    {
        string[] none = [];
        var both = ParseOwnsAddOnly(["docs/gaps.md"], ("A", ["docs/gaps.md"], none), ("B", ["docs/gaps.md"], none));

        Assert.Empty(CategoryOverlaps.Find(both));
        Assert.Equal(
            ["docs/gaps.md is owned by both A and B"],
            CategoryOverlaps.Find(ParseOwns(("A", ["docs/gaps.md"], none), ("B", ["docs/gaps.md"], none))));
    }

    // LANE-5: owns, "but not", alsoOwns, the add-only and re-record lists, and where categories are defined.
    [Fact]
    public void Categories_Brief_InPrompt()
    {
        var registry = new CategoryRegistry(new FakeLog(), new FakeFiles(), Rel);
        Assert.Equal("", registry.Brief("A"));

        var text = """
            {"lanes":{"D":{"name":"docs","owns":["docs/","README.md"],"except":["docs/jobs/"],"alsoOwns":"the wiki"}},
             "shared":{"addOnly":[".gitignore","notes.md"],"rerecord":["snap.txt","snap2.txt"],"rerecordRule":"gated change only"}}
            """;
        registry.Read(text, []);

        Assert.Equal(
            "Category D (docs) owns: docs/, README.md; but not: docs/jobs/; also the wiki."
            + " Any category may add to (not rewrite): .gitignore, notes.md."
            + " Any category may re-record, for its own gated change: snap.txt, snap2.txt (gated change only)."
            + " All categories are defined in docs/jobs/lanes.json.",
            registry.Brief("D"));
        Assert.Equal("", registry.Brief("Z"));
    }
}
