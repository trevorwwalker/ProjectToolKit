using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 992-B: categories are edited on the page, in a Categories panel that can be hidden as the Worker
/// types panel can (991-C), and the Lanes button that opened the file in its program is gone.
/// </summary>
public sealed class PageCategoriesPanelTests
{
    private static PageSnapshot WithCategories() => ViewsFixture.Full() with
    {
        Categories =
        [
            new PageCategory
            {
                Letter = "A", OriginalLetter = "A", Name = "core", Purpose = "Scheduling logic & cards", Color = "#336699",
                Owns = ["code/src/Scheduler.Core/", "test/unit/Scheduler.Core.Tests/"], Except = ["code/src/Scheduler.Core/WebContract/"],
            },
            new PageCategory { Letter = "B", OriginalLetter = "B", Name = "host", Purpose = "The machine", Owns = ["code/src/Scheduler.Host/"] },
        ],
        SharedAddOnly = [".gitignore", "docs/known_gaps.md"],
    };

    private static string Render(PageSnapshot snapshot, bool controlsAllowed = true) =>
        new PageRenderer().Render(snapshot, remote: !controlsAllowed, controlsAllowed);

    private static string Panel(string html)
    {
        var start = html.IndexOf("<form method='post' action='/categories'", StringComparison.Ordinal);
        Assert.True(start >= 0, "no Categories panel");
        var end = html.IndexOf("</form>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>992-B: the panel renders the file - each category's letter, name, purpose, colour, owns and except, one path per line, and the shared add-only list.</summary>
    [Fact]
    public void Page_CategoriesPanel_RendersTheFile()
    {
        var panel = Panel(Render(WithCategories()));

        Assert.Contains("<input type='hidden' name='corig' value='A'>", panel, StringComparison.Ordinal);
        Assert.Contains("name='cletter' value='A'", panel, StringComparison.Ordinal);
        Assert.Contains("name='cname' value='core'", panel, StringComparison.Ordinal);
        Assert.Contains(">Scheduling logic &amp; cards</textarea>", panel, StringComparison.Ordinal);
        Assert.Contains("name='ccolor' value='#336699'", panel, StringComparison.Ordinal);
        Assert.Contains(">code/src/Scheduler.Core/\ntest/unit/Scheduler.Core.Tests/</textarea>", panel, StringComparison.Ordinal);
        Assert.Contains(">code/src/Scheduler.Core/WebContract/</textarea>", panel, StringComparison.Ordinal);
        Assert.Contains("name='cletter' value='B'", panel, StringComparison.Ordinal);
        Assert.Contains("name='addonly'", panel, StringComparison.Ordinal);
        Assert.Contains(">.gitignore\ndocs/known_gaps.md</textarea>", panel, StringComparison.Ordinal);
        Assert.Contains(">Add category</button>", panel, StringComparison.Ordinal);
        Assert.Contains(">Remove</button>", panel, StringComparison.Ordinal);
        Assert.Contains(">Save</button>", panel, StringComparison.Ordinal);
        // The Add category row is a blank one with no letter it was loaded with, so Save sees it as new.
        Assert.Contains("<template id='cat-new'><tr class='catrow'><td><input type='hidden' name='corig' value=''>", panel, StringComparison.Ordinal);
    }

    /// <summary>992-B: hidden by default and remembered in the browser, with the categories as their chips on the summary line (970-C).</summary>
    [Fact]
    public void Page_CategoriesPanel_HiddenByDefault_SummaryIsChips()
    {
        var panel = Panel(Render(WithCategories()));

        Assert.Contains("class='wtypes wthidden cats'", panel, StringComparison.Ordinal);
        Assert.Contains("class='wttoggle' data-key='cats-open'", panel, StringComparison.Ordinal);
        var summary = panel[panel.IndexOf("wtsum", StringComparison.Ordinal)..panel.IndexOf("wtbody", StringComparison.Ordinal)];
        Assert.Contains(CategoryChip.Html(WithCategories().Categories, "A"), summary, StringComparison.Ordinal);
        Assert.Contains(CategoryChip.Html(WithCategories().Categories, "B"), summary, StringComparison.Ordinal);
    }

    /// <summary>992-B: the Lanes button (which said "Categories") is gone from the Control window.</summary>
    [Fact]
    public void Page_NoLanesButton()
    {
        var html = Render(WithCategories());

        Assert.DoesNotContain(">Categories</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Open the categories file - what each category is for", html, StringComparison.Ordinal);
    }

    /// <summary>992-B: a page that may not control the scheduler shows the panel with every field off.</summary>
    [Fact]
    public void Page_CategoriesPanel_Remote_Disabled()
    {
        var panel = Panel(Render(WithCategories(), controlsAllowed: false));

        Assert.Contains("name='cletter' value='A' size='2' maxlength='1' title='One letter, A to Z' disabled", panel, StringComparison.Ordinal);
        Assert.Contains("<button disabled title='Write these categories", panel, StringComparison.Ordinal);
    }
}
