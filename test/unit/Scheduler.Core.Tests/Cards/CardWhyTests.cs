// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// Why a card waits on the developer (CARD-13, L631-634): the text after <c>needs-human:</c> or
/// <c>review:</c> in Status, else the Status of a queue/active card that waits on the developer
/// without its <c>waiting:</c>, else - for a review card - the newest journal entry of its
/// <c>## Result</c>. <c>WhyFrom</c> says which of the two it came from.
/// Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class CardWhyTests
{
    private const string Jobs = "docs/jobs";

    private static Card Parse(CardFolder folder, string name, string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{FolderName(folder)}/{name}", text)
        ?? throw new InvalidOperationException($"not a card path: {name}");

    private static string FolderName(CardFolder folder) => folder switch
    {
        CardFolder.Queue => "queue",
        CardFolder.Active => "active",
        CardFolder.Review => "review",
        _ => "done",
    };

    // CARD-13.
    [Fact]
    public void Cards_Why_FromStatusOrResult()
    {
        // (a) the text after `needs-human:`.
        var parked = Parse(CardFolder.Active, "010-A-x.md", "# T\nStatus: needs-human: pick one");
        Assert.Equal("pick one", parked.Why);
        Assert.Equal("status", parked.WhyFrom);

        // (b) a card waiting on the developer: its Status without the leading `waiting:`.
        var waiting = Parse(CardFolder.Queue, "010-A-x.md", "# T\nStatus: waiting: developer to test");
        Assert.Equal("developer to test", waiting.Why);
        Assert.Equal("status", waiting.WhyFrom);

        // (c) a review card with no `review:` on its Status: the newest ## Result entry.
        var reviewed = Parse(
            CardFolder.Review,
            "010-A-x.md",
            """
            # T
            Status: review

            ## Result
            - 2026-11-18 09:00 old note
            - 2026-11-18 10:30 check the page
            """);
        Assert.Equal("- 2026-11-18 10:30 check the page", reviewed.Why);
        Assert.Equal("result", reviewed.WhyFrom);

        // (d) `review:` on the Status wins over the journal.
        var asked = Parse(CardFolder.Review, "010-A-x.md", "# T\nStatus: review: check X\n\n## Result\n- 2026-11-18 09:00 old note");
        Assert.Equal("check X", asked.Why);
        Assert.Equal("status", asked.WhyFrom);

        // Nothing to say: a plain queue card, and a review card whose Result has no entry at all
        // (L633: WhyFrom is only set when the journal had something).
        var ready = Parse(CardFolder.Queue, "010-A-x.md", "# T\nStatus: ready");
        Assert.Equal("", ready.Why);
        Assert.Equal("", ready.WhyFrom);
        var emptyReview = Parse(CardFolder.Review, "010-A-x.md", "# T\nStatus: review\n\n## Result\n");
        Assert.Equal("", emptyReview.Why);
        Assert.Equal("", emptyReview.WhyFrom);
    }
}
