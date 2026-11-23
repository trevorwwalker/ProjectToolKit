using System.Net;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// The two POST routes of the edited-template row (PAGE-9, roadmap item 1, job card 580-A):
/// <c>/template-overwrite</c> and <c>/template-keep</c> each reach their <c>IPageActions</c> method
/// with the project and the copy's path, from the query (the page's buttons) or the form (a plain one).
/// </summary>
public sealed class PageTemplateRoutesTests
{
    // PAGE-9: both routes queue their action, answered 303 to a form and 202 to the page script.
    [Theory]
    [InlineData("/template-overwrite")]
    [InlineData("/template-keep")]
    public async Task Routes_BothTemplateActions_ReachTheHandlerWithProjectAndFile(string path)
    {
        using var web = new WebHarness();

        var response = await web.PostFormAsync($"{path}?project=ptk&file=jobs%2FREADME.md");

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal(1, web.Actions.CallCount(path));
        Assert.Equal("ptk|jobs/README.md", web.Actions.LastTemplate);
    }

    // The same from a form body rather than the query, as a plain form posts it.
    [Theory]
    [InlineData("/template-overwrite")]
    [InlineData("/template-keep")]
    public async Task Routes_TemplateActions_ReadTheFormToo(string path)
    {
        using var web = new WebHarness();

        await web.PostFormAsync(path, "project=beta&file=docs%2Fknown_gaps.md");

        Assert.Equal(1, web.Actions.CallCount(path));
        Assert.Equal("beta|docs/known_gaps.md", web.Actions.LastTemplate);
    }

    // PAGE-1: the page script's POST gets the queued id, and only the route it used is counted.
    [Fact]
    public async Task Routes_TemplateActions_AnswerTheFetchWithAnId()
    {
        using var web = new WebHarness();

        var first = await web.PostFetchAsync("/template-overwrite?project=ptk&file=jobs%2FREADME.md");
        var second = await web.PostFetchAsync("/template-keep?project=ptk&file=jobs%2FREADME.md");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, web.Actions.CallCount("/template-overwrite"));
        Assert.Equal(1, web.Actions.CallCount("/template-keep"));
        Assert.Equal("ptk|jobs/README.md", web.Actions.LastTemplate);
    }
}
