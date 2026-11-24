using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1156-B: the review page's buttons send the card's project in a pool.</summary>
public sealed class ReviewPageProjectTests
{
    private static ReviewPanel Panel() => new()
    {
        Name = "650-C-review-panel.md",
        Questions =
        [
            new CardQuestion
            {
                Number = 1,
                Text = "Accept?",
                Choices = [new CardChoice { Label = "yes", Destination = CardFolder.Done }],
            },
        ],
    };

    [Fact]
    public void Render_InPool_NoteFormCarriesProject_SoEveryButtonSendsIt()
    {
        var html = new ReviewPage().Render(Panel(), new PageHealthView(), controlsAllowed: true, project: "Alpha & Co");

        Assert.Contains("<input type='hidden' name='project' value='Alpha &amp; Co'>", html);
        // Done, Send back and the answer buttons belong to the note form that holds the project.
        Assert.Contains("form='n-650-C-review-panel-md' formaction='/done'", html);
        Assert.Contains("form='n-650-C-review-panel-md' formaction='/resume-card'", html);
        Assert.Contains("form='n-650-C-review-panel-md' formaction='/answer?q=1'", html);
    }

    [Fact]
    public void Render_OneProject_SendsNoProject()
    {
        var html = new ReviewPage().Render(Panel(), new PageHealthView(), controlsAllowed: true);

        Assert.DoesNotContain("name='project'", html);
    }
}
