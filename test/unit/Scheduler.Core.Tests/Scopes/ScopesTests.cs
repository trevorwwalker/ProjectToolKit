using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Categories;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Scopes;

/// <summary>
/// Per-card scopes instead of categories (spec B2 SC-1 to SC-7). Test plan:
/// scheduler-next/test-plans/scopes.md. Spec B3 Q5, Q6, Q7 and Q10 as the developer answered them
/// on card 320-A (2026-11-18): a new <c>Scope:</c> line with the Locks rules (folders end in
/// <c>/</c>, an except list); add-only paths in categories.json and on the card; projects compared only
/// when they share a repository; no patterns, folders only; a redefined area's default simply applies.
/// Q7 was reversed on card 961-A (2026-11-19): <c>*</c>, <c>?</c> and <c>**</c> in a Scope, Locks or
/// categories.json path are matched by <see cref="PathGlob"/>.
/// </summary>
public class ScopesTests
{
    private static Card Card(string name, CardFolder folder = CardFolder.Queue)
    {
        var parts = name.Split('-');
        return new Card
        {
            Folder = folder,
            Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Status = "ready",
        };
    }

    private static DispatchPlan Plan(params Card[] cards) =>
        DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2);

    private static readonly Dictionary<string, Scope> Scopes = new()
    {
        ["020-B"] = Scope.Of("src/a.cs"),
        ["030-C"] = Scope.Of("src/b.cs"),
        ["040-D"] = Scope.Of("src/a.cs"),
    };

    private static Scope ScopeOf(Card c) => Scopes[c.Id];

    private static CategoriesFile Categories(CategoryDefinition area) =>
        new() { Categories = new Dictionary<string, CategoryDefinition> { [area.Letter] = area } };

    // SC-2, SC-3: cards on different files run side by side.
    [Fact]
    public void Scope_NoOverlap_RunsSideBySide()
    {
        var running = new Dictionary<string, Scope> { ["020-B"] = Scope.Of("src/a.cs") };
        var gated = ScopeGate.Apply(Plan(Card("030-C-b")), ScopeOf, running);

        Assert.Equal(["030-C"], gated.Ordered.Select(c => c.Card.Id));
        Assert.Empty(gated.Notes);
    }

    // SC-2, SC-3: cards on the same file run one after another.
    [Fact]
    public void Scope_SameFile_RunsAfter()
    {
        var running = new Dictionary<string, Scope> { ["020-B"] = Scope.Of("src/a.cs") };
        var gated = ScopeGate.Apply(Plan(Card("040-D-a")), ScopeOf, running);

        Assert.Empty(gated.Ordered);
        var note = Assert.Single(gated.Notes);
        Assert.Equal("040-D-a", note.Card);   // notes are keyed by card name, as the planner's (560-A)
        Assert.Contains("020-B", note.Note, StringComparison.Ordinal);
        Assert.Contains("src/a.cs", note.Note, StringComparison.Ordinal);

        // 020-B finished: nothing running holds src/a.cs any more.
        var after = ScopeGate.Apply(Plan(Card("040-D-a")), ScopeOf, new Dictionary<string, Scope>());
        Assert.Equal(["040-D"], after.Ordered.Select(c => c.Card.Id));
    }

    // SC-7: the scheduler, not the worker, prevents the second start (unlike
    // Dispatch_LocksLineIgnored today): of two ready cards on one file, only the first is offered,
    // and the one behind it is noted as waiting for it.
    [Fact]
    public void Scope_CheckedByScheduler_AtDispatch()
    {
        var plan = Plan(Card("020-B-a"), Card("040-D-a"), Card("030-C-b"));
        Assert.Equal(3, plan.Ordered.Count);

        var gated = ScopeGate.Apply(plan, ScopeOf, new Dictionary<string, Scope>());

        Assert.Equal(["020-B", "030-C"], gated.Ordered.Select(c => c.Card.Id));
        var note = Assert.Single(gated.Notes, n => n.Card == "040-D-a");
        Assert.Contains("020-B", note.Note, StringComparison.Ordinal);
    }

    // SC-4: a card without its own scope takes its area's: the area's owns minus its excepts.
    [Fact]
    public void Scope_NoOwnScope_TakesAreaDefault()
    {
        var categories = Categories(new CategoryDefinition
        {
            Letter = "D",
            Name = "docs",
            Owns = ["scheduler-next/spec.md", "scheduler-next/test-plans/"],
            Except = ["scheduler-next/test-plans/old/"],
        });

        var scope = AreaScope.For(Card("001-D-test-plan"), own: null, categories);

        Assert.NotNull(scope);
        Assert.Equal(["scheduler-next/spec.md", "scheduler-next/test-plans/"], scope.Paths);
        Assert.Equal(["scheduler-next/test-plans/old/"], scope.Except);

        var own = Scope.Of("scheduler-next/spec.md");
        Assert.Same(own, AreaScope.For(Card("001-D-test-plan"), own, categories));
        Assert.Null(AreaScope.For(Card("002-Z-none"), own: null, categories));
    }

    // SC-4, LANE-5: the prompt still carries the area's brief of what it owns (as
    // Categories_Brief_InPrompt).
    [Fact]
    public void Scope_AreaBriefInPrompt()
    {
        var categories = Categories(new CategoryDefinition
        {
            Letter = "D",
            Name = "docs",
            Owns = ["docs/", "README.md"],
            Except = ["docs/jobs/"],
            AlsoOwns = "the wiki",
        }) with { AddOnly = [".gitignore"] };

        Assert.Equal(
            "Category D (docs) owns: docs/, README.md; but not: docs/jobs/; also the wiki."
            + " Any category may add to (not rewrite): .gitignore."
            + " All categories are defined in docs/jobs/lanes.json.",
            AreaScope.Brief(categories, "D", "docs/jobs/lanes.json"));
        Assert.Equal(CategoryBrief.For(categories, "D", "docs/jobs/lanes.json"), AreaScope.Brief(categories, "D", "docs/jobs/lanes.json"));
        Assert.Equal("", AreaScope.Brief(null, "D", "docs/jobs/lanes.json"));
    }

    private static Scope? Parse(string scopeLine) => ScopeLine.Parse($"# 500-B: x\n\nLane: B\n{scopeLine}\n");

    // SC-1, Q5: the Scope line lists folders (ending in /), files and globs (961-A).
    [Fact]
    public void Scope_CardNamesScope_Parsed()
    {
        var entries = ScopeLine.Entries("# 500-B: x\n\nScope: src/, docs/a.md, src/*.cs\n");
        Assert.NotNull(entries);
        Assert.Equal(
            [new ScopeEntry("src/", ScopeEntryKind.Folder), new ScopeEntry("docs/a.md", ScopeEntryKind.File), new ScopeEntry("src/*.cs", ScopeEntryKind.Pattern)],
            entries);

        var scope = Parse("Scope: src/, docs/known_gaps.md (add-only); except src/old/");
        Assert.NotNull(scope);
        Assert.Null(scope.Problem);
        Assert.Equal(["src/", "docs/known_gaps.md"], scope.Paths);
        Assert.Equal(["src/old/"], scope.Except);
        Assert.Equal(["docs/known_gaps.md"], scope.AddOnly);

        // No Scope line: the card has no scope of its own.
        Assert.Null(ScopeLine.Entries("# 500-B: x\n\nLocks: none\n"));
        Assert.Null(Parse("Locks: src/"));
    }

    // SC-1, Q5: an empty Scope line and no area: not dispatched, with a note that the scope is missing.
    [Fact]
    public void Scope_EmptyScope_Rejected()
    {
        Assert.Null(Parse("Scope:"));
        Assert.Null(Parse("Scope: none"));

        var categories = Categories(new CategoryDefinition { Letter = "D", Owns = ["docs/"] });
        var card = Card("050-Z-empty");
        Assert.Null(AreaScope.Resolve(card, "# 050-Z: empty\n\nScope:\n", categories));

        var gated = ScopeGate.Apply(Plan(card), c => AreaScope.Resolve(c, "Scope:\n", categories), new Dictionary<string, Scope>());

        Assert.Empty(gated.Ordered);
        var note = Assert.Single(gated.Notes);
        Assert.Equal("050-Z-empty", note.Card);
        Assert.Contains("scope is missing", note.Note, StringComparison.Ordinal);
    }

    // SC-2, Q5: a folder (trailing /) overlaps every path inside it, except its except entries.
    [Fact]
    public void Scope_FolderContainsFile_Overlaps()
    {
        var running = new Dictionary<string, Scope> { ["020-B"] = Scope.Of("src/") };
        var gated = ScopeGate.Apply(Plan(Card("040-D-a")), _ => Scope.Of("src/x/y.cs"), running);

        Assert.Empty(gated.Ordered);
        Assert.Contains("src/x/y.cs", Assert.Single(gated.Notes).Note, StringComparison.Ordinal);

        Assert.Equal("src/x/y.cs", ScopeOverlap.Shared(Scope.Of("src/x/y.cs"), Scope.Of("src/")));
        Assert.Equal("src/x/", ScopeOverlap.Shared(Scope.Of("src/"), Scope.Of("src/x/")));
        Assert.Null(ScopeOverlap.Shared(Scope.Of("src/"), Scope.Of("srcx/a.cs")));
        Assert.Null(ScopeOverlap.Shared(Scope.Of("src"), Scope.Of("src/x/y.cs")));
        Assert.Null(ScopeOverlap.Shared(new Scope(["src/"], ["src/x/"]), Scope.Of("src/x/y.cs")));
        Assert.Null(ScopeOverlap.Shared(Scope.Of("src/x/y.cs"), new Scope(["src/"], ["src/x/"])));
    }

    // SC-2, Q7 as the developer reversed it on card 961-A (2026-11-19, for AmigaX): a Scope or Locks
    // path may be a glob. `*` is any run inside one path segment, `?` is one character, `**` crosses
    // folders. Such a scope is used, not refused. Each case is checked both ways round, by hand and
    // at dispatch.
    [Theory]
    [InlineData("src/*", "src/a.c", true)]
    [InlineData("src/*", "src/x/a.c", false)]
    [InlineData("src/**", "src/x/a.c", true)]
    [InlineData("*.md", "docs/a.md", false)]
    [InlineData("src/a*", "src/*b", true)]
    [InlineData("src/?.c", "src/a.c", true)]
    [InlineData("src/?.c", "src/ab.c", false)]
    [InlineData("vm/MemoryMap.*", "vm/MemoryMap.cpp", true)]
    [InlineData("vm/MemoryMap.*", "vm/Ram.cpp", false)]
    [InlineData("vm/**", "vm/x/Ram.cpp", true)]
    [InlineData("src/**/a.c", "src/x/a.c", true)]
    [InlineData("src/**/a.c", "src/a.c", true)]
    public void Scope_GlobOverlap_Rules(string pattern, string other, bool overlaps)
    {
        var scope = Parse($"Scope: {pattern}");
        Assert.NotNull(scope);
        Assert.Null(scope!.Problem);
        var otherScope = Parse($"Scope: {other}")!;

        var shared = ScopeOverlap.Shared(scope, otherScope);
        Assert.Equal(overlaps, shared is not null);
        Assert.Equal(overlaps, ScopeOverlap.Shared(otherScope, scope) is not null);
        if (overlaps && !PathGlob.IsGlob(other))
        {
            Assert.Equal(other, shared);     // the note names the concrete path
        }

        var gated = ScopeGate.Apply(
            Plan(Card("040-D-a"), Card("030-C-b")),
            c => c.Id == "040-D" ? scope : otherScope,
            new Dictionary<string, Scope>());
        string[] want = overlaps ? ["030-C"] : ["030-C", "040-D"];   // the planner offers 030-C first
        Assert.Equal(want, gated.Ordered.Select(c => c.Card.Id));
        if (overlaps)
        {
            Assert.Contains(other, Assert.Single(gated.Notes, n => n.Card == "040-D-a").Note, StringComparison.Ordinal);
        }
    }

    // SC-2, 961-A: a glob against a folder overlaps when the folder holds a path the glob matches.
    [Theory]
    [InlineData("src/*", "src/x/", false)]
    [InlineData("src/**", "src/x/", true)]
    [InlineData("src/x*", "src/x/", false)]
    [InlineData("src/x*", "src/xy/", false)]
    [InlineData("*", "docs/", false)]
    [InlineData("docs/**", "docs/a/", true)]
    [InlineData("testplan/*-unittest.*", "testplan/", true)]
    public void Scope_GlobAgainstFolder_OverlapsWhenItHoldsAMatch(string glob, string folder, bool overlaps)
    {
        var a = Scope.Of(glob);
        var b = Scope.Of(folder);

        Assert.Equal(overlaps, ScopeOverlap.Shared(a, b) is not null);
        Assert.Equal(overlaps, ScopeOverlap.Shared(b, a) is not null);
    }

    // SC-3, 961-A: an except entry leaves an entry out when it covers it. Where covering cannot be
    // shown (a glob over a folder, or over another glob) the overlap stays and the cards keep apart.
    [Fact]
    public void Scope_GlobExcept_LeavesOutWhatItCovers()
    {
        Assert.Null(ScopeOverlap.Shared(new Scope(["vm/"], ["vm/gen*"]), Scope.Of("vm/gen1.cpp")));
        Assert.NotNull(ScopeOverlap.Shared(new Scope(["vm/"], ["vm/gen*"]), Scope.Of("vm/board.cpp")));
        Assert.Null(ScopeOverlap.Shared(
            new Scope(["docs/agents/scouting/s*"], ["docs/agents/scouting/survey*"]),
            Scope.Of("docs/agents/scouting/survey-copper.md")));
        Assert.NotNull(ScopeOverlap.Shared(new Scope(["vm/"], ["vm/gen*/"]), Scope.Of("vm/gen1/")));
    }

    // SC-1, 961-A: a glob entry is read, its note in parentheses ignored; a character class is the
    // one pattern the matcher does not know, and it is the one thing still refused.
    [Fact]
    public void Scope_GlobEntries_ReadAndRefused()
    {
        var glob = Parse("Scope: vm/MemoryMap.* (only the map, 961-A), vm/Gary.*; except vm/gen*/")!;
        Assert.Null(glob.Problem);
        Assert.Equal(["vm/MemoryMap.*", "vm/Gary.*"], glob.Paths);
        Assert.Equal(["vm/gen*/"], glob.Except);

        var classes = Parse("Scope: src/[abc].c")!;
        Assert.NotNull(classes.Problem);
        Assert.Contains("src/[abc].c", classes.Problem, StringComparison.Ordinal);
    }

    // SC-1 (961-A): a Locks path may be a glob too: it joins the card's scope and blocks the files
    // it matches, and only a lock that could reach into an area's except entry drops that entry.
    [Fact]
    public void Scope_GlobLocks_JoinTheScopeAndDropTheExceptTheyReach()
    {
        var categories = Categories(new CategoryDefinition { Letter = "A", Owns = ["vm/"], Except = ["vm/gen/", "vm/Gary.*"] });
        var card = Card("010-A-lock") with { LocksLine = "vm/MemoryMap.*" };

        var scope = CardScope.Of(card, categories)!;

        Assert.Null(scope.Problem);
        Assert.Equal(["vm/", "vm/MemoryMap.*"], scope.Paths);
        Assert.Equal(["vm/gen/", "vm/Gary.*"], scope.Except);
        Assert.Equal("vm/MemoryMap.cpp", ScopeOverlap.Shared(scope, Scope.Of("vm/MemoryMap.cpp"), categories.AddOnly));
        Assert.Null(ScopeOverlap.Shared(scope, Scope.Of("vm/Gary.cpp"), categories.AddOnly));

        var reaching = card with { LocksLine = "vm/gen/fix.cpp" };
        Assert.Equal(["vm/Gary.*"], CardScope.Of(reaching, categories)!.Except);
    }

    // SC-2, Q6: projects in different repositories never overlap; projects sharing one repository
    // are compared by their paths in it.
    [Fact]
    public void Scope_AnyProject_Overlap()
    {
        var p = Scope.Of("src/a.cs") with { Repo = @"C:\Projects\P" };
        var o = Scope.Of("src/a.cs") with { Repo = @"C:\Projects\O" };
        Assert.Null(ScopeOverlap.Shared(p, o));

        var running = new Dictionary<string, Scope> { ["020-B"] = o };
        Assert.Equal(["040-D"], ScopeGate.Apply(Plan(Card("040-D-a")), _ => p, running).Ordered.Select(c => c.Card.Id));

        // Two projects in one repository (the same folder, written two ways).
        var p1 = Scope.Of("games/p/src/a.cs") with { Repo = @"C:\Projects\Mono\" };
        var o1 = Scope.Of("games/o/") with { Repo = "c:/projects/mono" };
        var o2 = Scope.Of("games/") with { Repo = "c:/projects/mono" };
        Assert.Null(ScopeOverlap.Shared(p1, o1));
        Assert.Equal("games/p/src/a.cs", ScopeOverlap.Shared(p1, o2));
    }

    // SC-5, Q5 (answered: both): overlap is allowed on add-only files, declared in categories.json's
    // shared add-only list or marked add-only on both cards' Scope lines; there the prompt says to
    // pull, rebase, resolve and re-run the gates.
    [Fact]
    public void Scope_AllowedOverlap_AddOnlyFile()
    {
        var marked = Parse("Scope: docs/known_gaps.md (add-only)")!;
        var both = ScopeGate.Apply(Plan(Card("020-B-a"), Card("030-C-b")), _ => marked, new Dictionary<string, Scope>());
        Assert.Equal(["020-B", "030-C"], both.Ordered.Select(c => c.Card.Id));

        var plain = Scope.Of("docs/known_gaps.md");
        var fromCategories = ScopeGate.Apply(Plan(Card("040-D-a")), _ => plain, new Dictionary<string, Scope> { ["020-B"] = plain }, addOnly: ["docs/known_gaps.md"]);
        Assert.Equal(["040-D"], fromCategories.Ordered.Select(c => c.Card.Id));

        // Marked on one card only: the other expects the file to itself.
        Assert.Equal("docs/known_gaps.md", ScopeOverlap.Shared(marked, plain));

        // The normal pair runs one after the other.
        var normal = ScopeGate.Apply(Plan(Card("020-B-a"), Card("040-D-a")), ScopeOf, new Dictionary<string, Scope>(), addOnly: ["docs/known_gaps.md"]);
        Assert.Equal(["020-B"], normal.Ordered.Select(c => c.Card.Id));

        Assert.Equal(
            "Other cards may change docs/known_gaps.md at the same time: add to it, never rewrite it;"
            + " before each push, pull and rebase, resolve any conflict, and re-run the gates.",
            ScopeBrief.AddOnly(marked, ["docs/known_gaps.md"]));
        Assert.Equal(ScopeBrief.AddOnly(marked, []), ScopeBrief.AddOnly(plain, ["docs/known_gaps.md"]));
        Assert.Equal("", ScopeBrief.AddOnly(Scope.Of("src/a.cs"), ["docs/known_gaps.md"]));
    }

    // LANE-2, SC-4, Q10 (answered: the new default simply applies): an area redefined while a card
    // of it without its own scope waits is not held; the card takes the new default at once.
    [Fact]
    public void Scope_HeldAreaWhenRedefined()
    {
        var before = Categories(new CategoryDefinition { Letter = "D", Owns = ["docs/"] });
        var after = Categories(new CategoryDefinition { Letter = "D", Owns = ["src/"] });
        var card = Card("060-D-docs");
        var running = new Dictionary<string, Scope> { ["020-B"] = Scope.Of("src/a.cs") };

        var old = ScopeGate.Apply(Plan(card), c => AreaScope.For(c, null, before), running);
        Assert.Equal(["060-D"], old.Ordered.Select(c => c.Card.Id));

        Assert.Equal(["src/"], AreaScope.For(card, null, after)!.Paths);
        var redefined = ScopeGate.Apply(Plan(card), c => AreaScope.For(c, null, after), running);
        Assert.Empty(redefined.Ordered);
        var note = Assert.Single(redefined.Notes).Note;
        Assert.Contains("waits: overlaps 020-B", note, StringComparison.Ordinal);
        Assert.DoesNotContain("held", note, StringComparison.OrdinalIgnoreCase);
    }
}
