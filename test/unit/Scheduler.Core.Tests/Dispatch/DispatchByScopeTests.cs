using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Launch;
using Scheduler.Core.Categories;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Dispatch;

/// <summary>
/// Dispatch by scope, not by category (job card 560-A; spec B2 SC-1 to SC-5, answers on 320-A and
/// 330-B). The card's scope is its Scope line, else its area's owns in categories.json, plus its Locks
/// paths; a ready card starts when its scope overlaps no running or earlier card's scope.
/// </summary>
public class DispatchByScopeTests
{
    private const string Jobs = "jobs";

    private static Card Card(CardFolder folder, string name, params string[] header)
    {
        var text = $"# {name}\n\nStatus: ready\n{string.Join("\n", header)}\n\n## Task\n\nx\n";
        var path = $"{Jobs}/{folder.ToString().ToLowerInvariant()}/{name}.md";
        return CardFiles.Parse(Jobs, path, text) ?? throw new InvalidOperationException(path);
    }

    private static RunningWorker Running(int slot, Card card) => new()
    {
        Slot = slot,
        Pid = 100 + slot,
        Start = DateTimeOffset.UnixEpoch,
        Card = card.Name,
        Category = card.Category,
        Id = card.Id,
    };

    private static CategoriesFile Categories(params string[] addOnly) => new()
    {
        Categories = new Dictionary<string, CategoryDefinition>(Text.Comparer)
        {
            ["A"] = new() { Letter = "A", Name = "core", Owns = ["src/core/", "test/core/"] },
            ["C"] = new() { Letter = "C", Name = "web", Owns = ["src/web/"] },
        },
        AddOnly = addOnly,
    };

    private static DispatchPlan Plan(IReadOnlyList<Card> cards, CategoriesFile? categories, params RunningWorker[] running) =>
        DispatchPlanner.Plan(cards, running, new Dictionary<string, int>(), categories, 2);

    private static string[] Ordered(DispatchPlan plan) => [.. plan.Ordered.Select(c => c.Card.Id)];

    private static string? NoteOf(DispatchPlan plan, string name) =>
        plan.Notes.SingleOrDefault(n => n.Card == name)?.Note;

    // SC-2, SC-3: two cards of one area with disjoint Scope lines both start.
    [Fact]
    public void Scope_SameAreaDisjointScopes_BothStart()
    {
        var cards = new[]
        {
            Card(CardFolder.Queue, "010-A-one", "Scope: src/core/a/"),
            Card(CardFolder.Queue, "020-A-two", "Scope: src/core/b/"),
        };

        Assert.Equal(["010-A", "020-A"], Ordered(Plan(cards, Categories())));
    }

    // SC-3, item 7: overlapping scopes run one after the other, and the Queue note names the
    // running card and the shared path, not "category A busy".
    [Fact]
    public void Scope_Overlapping_RunOneAfterAnother_NoteNamesTheCard()
    {
        var running = Card(CardFolder.Active, "010-A-running", "Scope: src/core/");
        var next = Card(CardFolder.Queue, "020-A-next", "Scope: src/core/x.cs");

        var plan = Plan([running, next], Categories(), Running(1, running));
        Assert.Empty(plan.Ordered);
        Assert.Equal("waits: overlaps 010-A on src/core/x.cs", NoteOf(plan, "020-A-next"));

        var after = Plan([running with { Folder = CardFolder.Done }, next], Categories());
        Assert.Equal(["020-A"], Ordered(after));
    }

    // SC-3: an earlier candidate reserves its scope, so of two queued overlapping cards only the
    // first starts; the second's note names it.
    [Fact]
    public void Scope_TwoQueuedOverlapping_FirstOnly()
    {
        var cards = new[]
        {
            Card(CardFolder.Queue, "010-A-one"),
            Card(CardFolder.Queue, "020-A-two"),
        };

        var plan = Plan(cards, Categories());
        Assert.Equal(["010-A"], Ordered(plan));
        Assert.Equal("waits: overlaps 010-A on src/core/", NoteOf(plan, "020-A-two"));
    }

    // SC-1, SC-4 (item 2): a card's Locks paths are added to its scope, so they block.
    [Fact]
    public void Scope_LocksPath_Blocks()
    {
        var locker = Card(CardFolder.Active, "010-A-locker", "Locks: src/web/views/ (wiring and labels only), `src/web/x.cs`");
        var web = Card(CardFolder.Queue, "020-C-web");

        var plan = Plan([locker, web], Categories(), Running(1, locker));
        Assert.Empty(plan.Ordered);
        Assert.Equal("waits: overlaps 010-A on src/web/views/", NoteOf(plan, "020-C-web"));

        var scope = CardScope.Of(locker, Categories())!;
        Assert.Equal(["src/core/", "test/core/", "src/web/views/", "src/web/x.cs"], scope.Paths);
        Assert.Null(scope.Problem);
    }

    // SC-1: "Locks: none" adds nothing, and a Locks note with commas inside parentheses is one entry.
    [Fact]
    public void Scope_LocksLine_NoneAndNotes()
    {
        Assert.Equal(["src/core/", "test/core/"], CardScope.Of(Card(CardFolder.Queue, "010-A-x", "Locks: none"), Categories())!.Paths);
        var noted = Card(CardFolder.Queue, "010-A-x", "Locks: src/web/a/ (the command line, environment and prompt), src/web/b/");
        Assert.Equal(["src/core/", "test/core/", "src/web/a/", "src/web/b/"], CardScope.Of(noted, Categories())!.Paths);
    }

    // SC-5: add-only paths in categories.json's shared list, or marked on both cards, never block.
    [Fact]
    public void Scope_AddOnlyPaths_DoNotBlock()
    {
        var shared = new[]
        {
            Card(CardFolder.Queue, "010-A-one", "Scope: src/core/a.cs, docs/known_gaps.md"),
            Card(CardFolder.Queue, "020-C-two", "Scope: src/web/b.cs, docs/known_gaps.md"),
        };
        Assert.Equal(["010-A", "020-C"], Ordered(Plan(shared, Categories("docs/known_gaps.md"))));
        Assert.Equal(["010-A"], Ordered(Plan(shared, Categories())));

        var marked = new[]
        {
            Card(CardFolder.Queue, "010-A-one", "Scope: src/core/a.cs, docs/notes.md (add-only)"),
            Card(CardFolder.Queue, "020-C-two", "Scope: src/web/b.cs, docs/notes.md (add-only)"),
        };
        Assert.Equal(["010-A", "020-C"], Ordered(Plan(marked, Categories())));
    }

    // SC-4, 320-A Q5 (item 4): a redefined area's cards dispatch at once with its new default
    // scope; nothing waits for Accept.
    [Fact]
    public void Scope_RedefinedArea_DispatchesWithoutAccept()
    {
        var registry = new CategoryRegistry(new NullLog(), new NoFiles(), "jobs/lanes.json");
        const string v1 = """{"lanes":{"A":{"name":"core","owns":["src/core/"],"except":[]}}}""";
        const string v2 = """{"lanes":{"A":{"name":"core","owns":["src/core/","src/more/"],"except":[]}}}""";
        var card = Card(CardFolder.Queue, "010-A-x");
        registry.Read(v1, []);
        registry.Read(v2, [card]);

        var plan = Plan([card], registry.Current);
        Assert.Equal(["010-A"], Ordered(plan));
        Assert.Equal(["src/core/", "src/more/"], CardScope.Of(card, registry.Current)!.Paths);
        Assert.DoesNotContain(plan.Notes, n => n.Note.Contains("held", StringComparison.Ordinal));
    }

    // SC-2, DISP-3: a card a worker is running is not started again; an orphan (active, nobody
    // running it) resumes, and a running card of its area with another scope does not stop it.
    [Fact]
    public void Scope_OrphanResumes_RunningCardNotRestarted()
    {
        var running = Card(CardFolder.Active, "010-A-run", "Scope: src/core/a/");
        var orphan = Card(CardFolder.Active, "020-A-orphan", "Scope: src/core/b/");

        var plan = Plan([running, orphan], Categories(), Running(1, running));
        var only = Assert.Single(plan.Ordered);
        Assert.Equal("020-A", only.Card.Id);
        Assert.True(only.Resume);
    }

    // SC-4: without categories.json a card's area letter is its scope, so one card per area runs, as before.
    [Fact]
    public void Scope_NoCategoriesFile_AreaLetterIsTheScope()
    {
        var cards = new[]
        {
            Card(CardFolder.Queue, "010-A-one"),
            Card(CardFolder.Queue, "020-A-two"),
            Card(CardFolder.Queue, "030-B-three"),
        };

        var plan = Plan(cards, null);
        Assert.Equal(["010-A", "030-B"], Ordered(plan));
        Assert.StartsWith("waits: overlaps 010-A", NoteOf(plan, "020-A-two"), StringComparison.Ordinal);
    }

    // SC-4, SC-5 (item 5): the worker's brief names the card's scope, not the category's ownership.
    [Fact]
    public void Scope_Brief_ListsTheScope()
    {
        var card = Card(CardFolder.Queue, "010-A-x", "Scope: src/core/a/, docs/notes.md (add-only); except src/core/a/old/", "Locks: src/web/v/");
        var brief = ScopeBrief.For(card, Categories("docs/known_gaps.md"), "jobs/lanes.json");

        Assert.Contains("You may change: src/core/a/, docs/notes.md, src/web/v/; but not: src/core/a/old/.", brief, StringComparison.Ordinal);
        Assert.Contains("Any card may add to (not rewrite): docs/known_gaps.md.", brief, StringComparison.Ordinal);
        Assert.Contains("Other cards may change docs/notes.md at the same time", brief, StringComparison.Ordinal);
        Assert.Contains("area A (core)", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("owns", brief, StringComparison.Ordinal);

        var config = new SchedulerConfig();
        var prompt = WorkerPrompt.Build(config, new WorkerPromptRequest
        {
            Card = card,
            Worktree = @"C:\w\slot1",
            Slot = 1,
            Start = DateTimeOffset.UnixEpoch,
            Jobs = 4,
            ScopeBrief = brief,
        });
        Assert.Contains(brief, prompt, StringComparison.Ordinal);
        Assert.Contains("stay inside the card's scope", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("lane A's file", prompt, StringComparison.Ordinal);
    }

    private sealed class NullLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    private sealed class NoFiles : IStateFiles
    {
        public string? Read(StateFile file) => null;

        public void Write(StateFile file, string text)
        {
        }
    }
}
