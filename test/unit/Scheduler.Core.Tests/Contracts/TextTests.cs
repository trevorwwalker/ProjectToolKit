using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Contracts;

public class TextTests
{
    // G-1 (with CARD-4, CARD-8, CARD-15, DISP-18).
    // Card reading and dispatch are later cards; this pins the Text helpers they use, on the
    // test plan's inputs: card 010-b-x.md, status READY, PRIORITY: High, worker OPUS vs opus.
    [Fact]
    public void Porting_TextCompares_AreCaseInsensitive()
    {
        // CARD-4: the card path pattern says [A-Z], and still matches category b.
        var m = Text.Match("docs/jobs/queue/010-b-x.md",
            @"^docs/jobs/(queue|active|review|done)/(\d+)-([A-Z])-[^/]+\.md$");
        Assert.True(m.Success);
        Assert.Equal("010", m.Groups[2].Value);
        Assert.Equal("b", m.Groups[3].Value);

        // Line keys and Status words (CARD-7, DISP-10): READY is ready.
        Assert.True(Text.IsMatch("status: READY", @"^Status:"));
        Assert.True(Text.IsMatch("READY", "^ready"));

        // CARD-8: PRIORITY: High is the word high.
        var p = Text.Match("PRIORITY: High", @"^Priority:\s*(\w+)");
        Assert.True(p.Success);
        Assert.True(Text.Same(p.Groups[1].Value, "high"));

        // CARD-15: waiting on the developer, any case.
        Assert.True(Text.IsMatch("WAITING: The Developer", @"^waiting:\s*(the\s+)?(developer|dev|you|human)\b"));

        // DISP-18: Worker: OPUS fits a worker named opus.
        Assert.True(Text.Same("OPUS", "opus"));
        Assert.False(Text.Same("opus", "sonnet"));
        Assert.False(Text.Same(null, "opus"));
        Assert.True(Text.Same(null, null));

        // -like, and Contains on hashtables keyed by string.
        Assert.False(Text.Like("010-B-X.md", "*readme*"));
        Assert.True(Text.Like("docs/jobs/requests/ReadMe.md", "*README*"));
        Assert.True(Text.Like("Slot4", "slot?"));
        var categories = new HashSet<string>(Text.Comparer) { "B" };
        Assert.Contains("b", categories);

        // Sort-Object: culture compare, case-insensitive (an ordinal sort puts C before a).
        var sorted = new[] { "b", "C", "a" }.Order(Text.SortComparer).ToArray();
        Assert.Equal(["a", "b", "C"], sorted);
    }
}
