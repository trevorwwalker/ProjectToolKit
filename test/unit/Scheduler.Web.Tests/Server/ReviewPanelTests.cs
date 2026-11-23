using System.Net;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Server;

/// <summary>The review panel <c>/review?card=</c> (roadmap item 6, job card 650-C).</summary>
public sealed class ReviewPanelTests
{
    private static ReviewPanel Panel() => new()
    {
        Name = "650-C-review-panel.md",
        Title = "650-C: Review panel",
        Status = "review: check the panel on the real page",
        Questions =
        [
            new CardQuestion
            {
                Number = 1,
                Text = "Accept the panel?",
                Choices = [new CardChoice { Label = "yes", Destination = CardFolder.Done }, new CardChoice { Label = "no" }],
            },
        ],
        Commits =
        [
            new ReviewCommit
            {
                Sha = "0123456789abcdef0123456789abcdef01234567",
                Subject = "650-C: review panel",
                Time = new DateTimeOffset(2026, 11, 19, 2, 30, 0, TimeSpan.Zero),
                Files = [new ReviewFile("code/src/Scheduler.Web/Views/ReviewPage.cs", 120, 3)],
            },
        ],
        Result = "## Done\n\n- built **the panel**\n- see `docs/shot.png`\n\n<script>alert(1)</script>",
        Links = ["docs/shot.png"],
    };

    private static async Task<string> GetReview(WebHarness web, string card = "650-C-review-panel.md")
    {
        var response = await web.Client.GetAsync("/review?card=" + Uri.EscapeDataString(card));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // Roadmap item 6: the panel shows the title, the Status text, the question with an answer button per choice.
    [Fact]
    public async Task Panel_ShowsTitleStatusAndQuestionButtons()
    {
        using var web = new WebHarness();
        web.Data.Reviews["650-C-review-panel.md"] = Panel();

        var html = await GetReview(web);

        Assert.Contains("650-C: Review panel", html);
        Assert.Contains("review: check the panel on the real page", html);
        Assert.Contains("Q1: Accept the panel?", html);
        Assert.Contains("formaction='/answer?q=1'", html);
        Assert.Contains(">yes</button>", html);
        Assert.Contains(">no</button>", html);
    }

    // The commits (short sha, subject, time) and the files they changed with line counts.
    [Fact]
    public async Task Panel_ShowsCommitsWithFilesAndLineCounts()
    {
        using var web = new WebHarness();
        web.Data.Reviews["650-C-review-panel.md"] = Panel();

        var html = await GetReview(web);

        Assert.Contains("0123456", html);
        Assert.DoesNotContain("0123456789abcdef", html);
        Assert.Contains("650-C: review panel", html);
        Assert.Contains("2026-11-19 02:30", html);
        Assert.Contains("code/src/Scheduler.Web/Views/ReviewPage.cs", html);
        Assert.Contains("+120", html);
        Assert.Contains("-3", html);
    }

    // The health of main: green or red, with the commit it was checked at.
    [Theory]
    [InlineData(true, "green")]
    [InlineData(false, "red")]
    public async Task Panel_ShowsHealthOfMain(bool green, string word)
    {
        using var web = new WebHarness();
        web.Data.Reviews["650-C-review-panel.md"] = Panel();
        web.Data.Latest = web.Data.Latest with { Health = new PageHealthView { Green = green, Sha = "abcdef1234567" } };

        var html = await GetReview(web);

        Assert.Contains($">{word}</span> at <code>abcdef1</code>", html);
    }

    // The Result is rendered as Markdown, and nothing in it can add markup.
    [Fact]
    public async Task Panel_RendersResultMarkdown_AndEscapesHtml()
    {
        using var web = new WebHarness();
        web.Data.Reviews["650-C-review-panel.md"] = Panel();

        var html = await GetReview(web);

        Assert.Contains("<li>built <b>the panel</b></li>", html);
        Assert.Contains("<code>docs/shot.png</code>", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    // Files the Result names that exist are linked.
    [Fact]
    public async Task Panel_LinksFilesTheResultNames()
    {
        using var web = new WebHarness();
        web.Data.Reviews["650-C-review-panel.md"] = Panel();

        var html = await GetReview(web);

        Assert.Contains("href='/repo-file?path=docs%2Fshot.png'", html);
    }

    // /repo-file serves a file the host resolves, and refuses one it does not.
    [Fact]
    public async Task RepoFile_ServesResolvedFile_AndRefusesOthers()
    {
        using var web = new WebHarness();
        var file = web.WriteLog("shot.txt", "hello");
        web.Data.RepoFiles["docs/shot.txt"] = file;

        var ok = await web.Client.GetAsync("/repo-file?path=docs%2Fshot.txt");
        var no = await web.Client.GetAsync("/repo-file?path=..%2Fsecret.txt");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("hello", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, no.StatusCode);
    }

    // No such card: 404.
    [Fact]
    public async Task Panel_UnknownCard_IsNotFound()
    {
        using var web = new WebHarness();

        var response = await web.Client.GetAsync("/review?card=nope.md");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // PAGE-9: Done and Send back (with its note box) post the existing routes for the card.
    [Fact]
    public async Task Panel_DoneAndSendBack_PostTheExistingRoutes()
    {
        using var web = new WebHarness();
        web.Data.Reviews["650-C-review-panel.md"] = Panel();

        var html = await GetReview(web);

        Assert.Contains("formaction='/done'", html);
        Assert.Contains("formaction='/resume-card'", html);
        Assert.Contains("action='/note'", html);
        Assert.Contains("name='note'", html);

        await web.PostFormAsync("/done?card=650-C-review-panel.md");
        await web.PostFormAsync("/resume-card?card=650-C-review-panel.md", "note=more+tests");

        Assert.Equal(1, web.Actions.CallCount("/done"));
        Assert.Equal(1, web.Actions.CallCount("/resume-card"));
        Assert.Equal("more tests", web.Actions.LastNote);
    }

    // Another PC without remoteControl sees the buttons disabled.
    [Fact]
    public void Panel_ControlsNotAllowed_DisablesButtons()
    {
        var html = new ReviewPage().Render(Panel(), new PageHealthView(), controlsAllowed: false);

        Assert.DoesNotContain("action='/done", html);
        Assert.Contains("<button disabled>Done</button>", html);
    }

    // The Waiting row of a review card links to its panel.
    [Fact]
    public void WaitingRow_ReviewCard_LinksToPanel()
    {
        var snapshot = Views.ViewsFixture.Plain() with
        {
            Waiting = [new PageWaitingItem { Kind = PageWaitingKind.Review, Name = "650-C-review-panel.md", Note = "check" }],
        };

        var html = new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

        Assert.Contains("href='/review?card=650-C-review-panel.md'", html);
    }
}
