// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// A generic file whose copy was edited in the project shows under Waiting on you with Overwrite
/// (take the toolkit's template) and Keep (leave it, and stop asking until the template changes
/// again) - roadmap item 1, job card 580-A. The copy is not a card, so its row links nothing.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5, PAGE-9).
/// </summary>
public sealed class PageTemplateRowTests
{
    private const string File = "jobs/README.md";

    private static PageWaitingItem Row() => new()
    {
        Kind = PageWaitingKind.TemplateEdited,
        Name = File,
        Note = "ProjectToolkit: jobs/README.md was edited in this project",
    };

    private static string Render(params PageWaitingItem[] waiting) =>
        new PageRenderer().Render(
            ViewsFixture.Plain() with { Waiting = waiting },
            remote: false,
            controlsAllowed: true);

    // PAGE-5: the row, its two buttons, and no card link and no Open for a file that is not a card.
    [Fact]
    public void Page_EditedTemplate_RowWithOverwriteAndKeep()
    {
        var html = Render(Row());

        Assert.Contains(">template<", html, StringComparison.Ordinal);
        Assert.Contains("jobs/README.md was edited in this project", html, StringComparison.Ordinal);
        Assert.Contains("action='/template-overwrite?project=ProjectToolkit&file=jobs%2FREADME.md'", html, StringComparison.Ordinal);
        Assert.Contains(">Overwrite</button>", html, StringComparison.Ordinal);
        Assert.Contains("action='/template-keep?project=ProjectToolkit&file=jobs%2FREADME.md'", html, StringComparison.Ordinal);
        Assert.Contains(">Keep</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/open?card=jobs", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card='jobs", html, StringComparison.Ordinal);
    }

    // The buttons say what each choice does, so the developer can pick without reading the code.
    [Fact]
    public void Page_EditedTemplate_ButtonsSayWhatTheyDo()
    {
        var html = Render(Row());

        Assert.Contains("Take the toolkit&#39;s template for this file", html, StringComparison.Ordinal);
        Assert.Contains("stop asking until the toolkit&#39;s template changes again", html, StringComparison.Ordinal);
    }

    // PAGE-7: from another PC the two buttons are drawn disabled, as every other waiting control is.
    [Fact]
    public void Page_EditedTemplate_WithoutControls_ButtonsDisabled()
    {
        var html = new PageRenderer().Render(
            ViewsFixture.Plain() with { Waiting = [Row()] },
            remote: true,
            controlsAllowed: false);

        Assert.Contains("<button class='' disabled>Overwrite</button>", html, StringComparison.Ordinal);
        Assert.Contains("<button class='' disabled>Keep</button>", html, StringComparison.Ordinal);
    }

    // MP-8: the one Waiting on you across projects carries the row too, with the project it came from
    // on both buttons so the action reaches that project's scheduler.
    [Fact]
    public void CombinedPage_EditedTemplate_RowWithProjectOnItsButtons()
    {
        var snapshot = ViewsFixture.Plain() with { Project = "beta", Waiting = [Row()] };
        var html = new CombinedPageRenderer().Render([snapshot], "slate", remote: false, controlsAllowed: true);

        Assert.Contains(">template<", html, StringComparison.Ordinal);
        Assert.Contains("data-project='beta'", html, StringComparison.Ordinal);
        Assert.Contains("action='/template-overwrite' class='g' data-tip='Take the toolkit&#39;s template for this file, in one commit to main.'><input type='hidden' name='project' value='beta'><input type='hidden' name='file' value='jobs/README.md'>", html, StringComparison.Ordinal);
        Assert.Contains("action='/template-keep' class='g' data-tip='Leave the project&#39;s copy and stop asking until the toolkit&#39;s template changes again.'><input type='hidden' name='project' value='beta'><input type='hidden' name='file' value='jobs/README.md'>", html, StringComparison.Ordinal);
    }

    // The card rows are unaffected by the new kind and keep their link and Open (PAGE-5).
    [Fact]
    public void Page_TemplateRowAmongCardRows()
    {
        var html = Render(
            Row(),
            new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "070-A-x", Title = "The card", Note = "look" });

        Assert.Contains(">template<", html, StringComparison.Ordinal);
        Assert.Contains(">review<", html, StringComparison.Ordinal);
        Assert.Contains("data-card='070-A'", html, StringComparison.Ordinal);
        Assert.Contains("/open?card=070-A-x", html, StringComparison.Ordinal);
    }
}
