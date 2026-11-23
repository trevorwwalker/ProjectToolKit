using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// Which cards wait on the developer and which on the planner (CARD-15, CARD-16; L552, L558): only
/// cards in queue/ or active/ count, and the word after <c>waiting:</c> must be the whole word -
/// <c>developers</c> is not <c>developer</c>. Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class CardWaitsTests
{
    private const string Jobs = "docs/jobs";

    private static Card Card(CardFolder folder, string status) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{FolderName(folder)}/010-A-x.md", $"# T\nStatus: {status}")
        ?? throw new InvalidOperationException("not a card path");

    private static string FolderName(CardFolder folder) => folder switch
    {
        CardFolder.Queue => "queue",
        CardFolder.Active => "active",
        CardFolder.Review => "review",
        _ => "done",
    };

    // CARD-15.
    [Fact]
    public void Cards_WaitsOnDev_Patterns()
    {
        Assert.True(CardWaits.OnDeveloper(Card(CardFolder.Queue, "waiting: developer")));
        Assert.True(CardWaits.OnDeveloper(Card(CardFolder.Active, "waiting: developer")));
        Assert.True(CardWaits.OnDeveloper(Card(CardFolder.Queue, "waiting: the dev")));
        Assert.True(CardWaits.OnDeveloper(Card(CardFolder.Queue, "Waiting: YOU now")));
        Assert.True(CardWaits.OnDeveloper(Card(CardFolder.Queue, "waiting: human")));
        Assert.True(CardWaits.OnDeveloper(Card(CardFolder.Queue, "waiting:developer")));

        // \b: "developers" is neither "developer" nor "dev".
        Assert.False(CardWaits.OnDeveloper(Card(CardFolder.Queue, "waiting: developers")));

        // Only queue/ and active/ wait; a review card is finished, not waiting.
        Assert.False(CardWaits.OnDeveloper(Card(CardFolder.Review, "waiting: developer")));
        Assert.False(CardWaits.OnDeveloper(Card(CardFolder.Done, "waiting: developer")));
        Assert.False(CardWaits.OnDeveloper(Card(CardFolder.Queue, "ready")));
    }

    // CARD-16.
    [Fact]
    public void Cards_WaitsOnPlanner_Patterns()
    {
        Assert.True(CardWaits.OnPlanner(Card(CardFolder.Queue, "waiting: planner (request #3)")));
        Assert.True(CardWaits.OnPlanner(Card(CardFolder.Active, "waiting: the planner")));

        // \b: "planners" is not "planner".
        Assert.False(CardWaits.OnPlanner(Card(CardFolder.Queue, "waiting: planners")));

        // done/ never waits, and the developer is not the planner.
        Assert.False(CardWaits.OnPlanner(Card(CardFolder.Done, "waiting: planner")));
        Assert.False(CardWaits.OnPlanner(Card(CardFolder.Queue, "waiting: developer")));
    }
}
