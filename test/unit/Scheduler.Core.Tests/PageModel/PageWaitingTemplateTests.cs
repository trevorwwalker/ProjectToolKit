using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// The Waiting on you rows for a generic file whose copy was edited in the project (roadmap item 1,
/// job card 580-A): the scheduler does not overwrite it, so it waits here for the developer's
/// Overwrite (take the toolkit's template) or Keep (leave it until the template changes again).
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5).
/// </summary>
public class PageWaitingTemplateTests
{
    private static Card Card(string name) => new()
    {
        Folder = CardFolder.Active,
        Path = $"jobs/active/{name}.md",
        Name = name,
        Num = 70,
        Category = "A",
        Id = "070-A",
        Title = $"The {name} card",
        Status = "ready",
    };

    private static PageTemplateEdit Edit(string project, string target) => new(project, target);

    // PAGE-5, roadmap item 1: the edited copy is a row of its own kind, named by the copy's path and
    // worded with the project it belongs to.
    [Fact]
    public void Waiting_EditedTemplate_IsARowWithProjectFileAndNote()
    {
        var window = PageWaiting.Window([], editedTemplates: [Edit("ptk", "jobs/README.md")]);

        var row = Assert.Single(window);
        Assert.Equal(PageWaitingKind.TemplateEdited, row.Kind);
        Assert.Equal("jobs/README.md", row.Name);
        Assert.Equal("ptk: jobs/README.md was edited in this project", row.Note);
        Assert.Equal(PageWaiting.EditedTemplateNote("ptk", "jobs/README.md"), row.Note);
    }

    // Several projects and copies on one page (MP-8): the rows read by project, then by file, so the
    // combined window lists one project's copies together.
    [Fact]
    public void Waiting_EditedTemplates_AreOrderedByProjectThenFile()
    {
        var window = PageWaiting.Window([], editedTemplates:
        [
            Edit("beta", "jobs/README.md"),
            Edit("alpha", "jobs/project.md"),
            Edit("alpha", "docs/workflow.md"),
        ]);

        Assert.Equal(
            [
                "alpha: docs/workflow.md was edited in this project",
                "alpha: jobs/project.md was edited in this project",
                "beta: jobs/README.md was edited in this project",
            ],
            window.Select(i => i.Note).ToList());
    }

    // PAGE-5: the window still reads held categories first, then the edited copies, then the cards.
    [Fact]
    public void Waiting_HeldCategoriesLead_ThenEditedCopies_ThenCards()
    {
        var stuck = new[] { Card("070-A") };
        var window = PageWaiting.Window(
            [],
            stuck,
            new Dictionary<string, string> { ["B"] = "lane B's definition changed" },
            editedTemplates: [Edit("ptk", "jobs/README.md")]);

        Assert.Equal(
            [PageWaitingKind.HeldCategory, PageWaitingKind.TemplateEdited, PageWaitingKind.Stuck],
            window.Select(i => i.Kind).ToList());
    }

    // A project with no edited copy, or one whose templates this scheduler does not keep, gets no row
    // and the other rows are untouched.
    [Fact]
    public void Waiting_NoEditedCopies_NoTemplateRow()
    {
        var cards = new[] { Card("070-A") };

        var without = PageWaiting.Window(cards);
        var empty = PageWaiting.Window(cards, editedTemplates: []);
        var withOne = PageWaiting.Window(cards, editedTemplates: [Edit("ptk", "jobs/README.md")]);

        Assert.DoesNotContain(without, i => i.Kind == PageWaitingKind.TemplateEdited);
        Assert.Equal(without.Count, empty.Count);
        Assert.Equal(without.Count + 1, withOne.Count);
    }
}
