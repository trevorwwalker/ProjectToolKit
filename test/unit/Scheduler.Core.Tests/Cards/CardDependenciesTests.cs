using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// Whether a card's dependencies are finished (CARD-17, L559) and what it is therefore waiting for
/// (DISP-12, L2909-2910): a dependency counts as finished only when the card with that id is in
/// done/ - a card in review/, active/ or queue/ keeps its dependents waiting (1059-B) - and the
/// check goes one level only: two cards that depend on each other both wait forever, which is what
/// stops the scheduler looping on them.
/// Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class CardDependenciesTests
{
    private const string Jobs = "docs/jobs";

    private static Card Card(string folderAndName, string text = "") =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folderAndName}", text)
        ?? throw new InvalidOperationException($"not a card path: {folderAndName}");

    private static Card ParseQueue(string name, string text) => Card($"queue/{name}", text);

    // CARD-17 (1059-B): done/ releases, review/ does not.
    [Fact]
    public void Cards_DepsDone_OnlyDone()
    {
        var known = CardDependencies.ById(
        [
            Card("done/010-A-x.md"),
            Card("review/020-B-y.md", "Status: review: look"),
            Card("active/030-C-z.md", "Status: ready"),
        ]);

        var waiting = ParseQueue("050-E-w.md", "Status: ready\nDepends on: 010-A, 020-B, 030-C, 040-D");
        Assert.False(CardDependencies.Done(waiting, known));
        Assert.Equal(["020-B", "030-C", "040-D"], CardDependencies.Unfinished(waiting, known));

        // Only the done/ one releases.
        var ready = ParseQueue("050-E-w.md", "Status: ready\nDepends on: 010-A");
        Assert.True(CardDependencies.Done(ready, known));
        Assert.Empty(CardDependencies.Unfinished(ready, known));

        // A card whose every dependency is in review/ waits on all of them (1059-B: 1047-B started
        // one minute after the design card 1058-D moved to review/ with five open questions).
        var onReview = ParseQueue("050-E-w.md", "Status: ready\nDepends on: 020-B");
        Assert.False(CardDependencies.Done(onReview, known));
        Assert.Equal(["020-B"], CardDependencies.Unfinished(onReview, known));

        // An id no card has is unfinished, not finished.
        var missing = ParseQueue("050-E-w.md", "Status: ready\nDepends on: 040-D");
        Assert.False(CardDependencies.Done(missing, known));
        Assert.Equal(["040-D"], CardDependencies.Unfinished(missing, known));

        // A card with no Depends line depends on nothing, so it is finished to wait on (L559: the
        // loop has nothing to fail on).
        Assert.True(CardDependencies.Done(ParseQueue("050-E-w.md", "Status: ready"), known));

        // ById is the script's hashtable (L3017): the last card with an id is the one it holds.
        var duplicated = CardDependencies.ById([Card("queue/010-A-old.md", "Status: ready"), Card("active/010-A-new.md", "Status: ready")]);
        Assert.Equal("docs/jobs/active/010-A-new.md", duplicated["010-A"].Path);
    }

    // CARD-17 (1059-B): the same dependency releases when the developer accepts the review card
    // into done/, which is what the Done button does.
    [Fact]
    public void Cards_ReviewCard_ReleasesDependentsOnlyWhenItReachesDone()
    {
        var dependent = ParseQueue("060-F-w.md", "Status: ready\nDepends on: 020-B");

        var underReview = CardDependencies.ById([Card("review/020-B-y.md", "Status: review: look")]);
        Assert.False(CardDependencies.Done(dependent, underReview));
        Assert.Equal(["020-B"], CardDependencies.Unfinished(dependent, underReview));

        var accepted = CardDependencies.ById([Card("done/020-B-y.md")]);
        Assert.True(CardDependencies.Done(dependent, accepted));
        Assert.Empty(CardDependencies.Unfinished(dependent, accepted));
    }

    // CARD-4, 1059-B: archive/ is not one of the card folders, so a card swept there is not a card
    // at all and its Status cannot release a dependent.
    [Fact]
    public void Cards_ArchivedDependency_ReleasesNothing()
    {
        var byId = CardDependencies.ById([Card("queue/050-E-w.md", "Status: ready\nDepends on: 020-B")]);
        var dependent = byId["050-E"];
        Assert.False(CardDependencies.Done(dependent, byId));
        Assert.Equal(["020-B"], CardDependencies.Unfinished(dependent, byId));
    }

    // CARD-17, DISP-12 (L559 checks one level only, so a cycle never resolves and never loops).
    [Fact]
    public void Cards_DependencyCycle_BothWaitForever()
    {
        var first = ParseQueue("010-A-one.md", "# T\nStatus: ready\nDepends on: 020-B");
        var second = ParseQueue("020-B-two.md", "# T\nStatus: ready\nDepends on: 010-A");
        var byId = CardDependencies.ById([first, second]);
        var cards = new[] { first, second };

        // Neither passes the dependency check, so neither is a candidate and neither is started -
        // on this tick and on every later one, however often the tick is run (no exception, and no
        // walk that could go round the cycle).
        for (var tick = 0; tick < 3; tick++)
        {
            foreach (var card in cards)
            {
                Assert.False(CardDependencies.Done(card, byId));
            }
        }

        // What the page says about each (DISP-12, L2910).
        Assert.Equal("waits for 020-B", "waits for " + string.Join(", ", CardDependencies.Unfinished(first, byId)));
        Assert.Equal("waits for 010-A", "waits for " + string.Join(", ", CardDependencies.Unfinished(second, byId)));
    }
}
