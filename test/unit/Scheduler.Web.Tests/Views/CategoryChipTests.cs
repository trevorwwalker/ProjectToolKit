using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>970-C: the category chip.</summary>
public class CategoryChipTests
{
    private static readonly PageCategory[] Cats =
    [
        new() { Letter = "A", Name = "core", Purpose = "The scheduling logic & more" },
        new() { Letter = "C", Name = "web", Purpose = "The page" },
        new() { Letter = "X", Name = "averyverylongname", Purpose = "Long" },
        new() { Letter = "Y", Name = "own", Purpose = "Own colour", Color = "#112233" },
    ];

    private static string QueueOf(string card)
    {
        var snapshot = ViewsFixture.Plain() with
        {
            Categories = Cats,
            Queue = [new PageQueueItem { Name = card, Title = "t", Note = "next up" }],
        };
        return new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);
    }

    [Fact]
    public void QueueRow_HasChipWithNamePurposeAndColour()
    {
        var html = QueueOf("970-C-x.md");
        Assert.Contains(">web</span>", html);
        Assert.Contains("title='The page'", html);
        Assert.Contains($"background:{CategoryChip.Palette[1]}", html);
    }

    [Fact]
    public void Colour_IsTheSameOnASecondRender() =>
        Assert.Equal(QueueOf("970-C-x.md"), QueueOf("970-C-x.md"));

    [Fact]
    public void Purpose_IsEncodedInTheTooltip() =>
        Assert.Contains("title='The scheduling logic &amp; more'", QueueOf("100-A-x.md"));

    [Fact]
    public void LongName_IsCutWithAnEllipsis() =>
        Assert.Contains(">averyvery…</span>", QueueOf("5-X-x.md").Replace("averyvery…", "averyvery…"));

    [Fact]
    public void FileColour_WinsOverThePalette() =>
        Assert.Contains("background:#112233", QueueOf("5-Y-x.md"));

    [Fact]
    public void UnknownLetter_StillShowsTheLetter() =>
        Assert.Contains(">Q</span>", QueueOf("5-Q-x.md"));

    [Fact]
    public void Palette_ReadsWithWhiteTextInEveryTheme()
    {
        foreach (var colour in CategoryChip.Palette)
        {
            Assert.True(CategoryChip.ContrastWithWhite(colour) >= 4.5, colour);
        }
    }
}
