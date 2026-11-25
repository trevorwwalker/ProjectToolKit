// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using Scheduler.Core.Views;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>The plan document <c>/plan?path=</c> (job card 1113-C, views.md Plans second pass).</summary>
public sealed class PlanPageTests
{
    private const string Path = "docs/ai/plan.md";

    private const string Plan = """
        # The plan

        Intro with `code` and **bold**.

        ## First step

        - one
        - two

        ### Part A

        ```
        ## not a heading
        ```

        ## First step

        | Name | Value |
        |------|-------|
        | a | 1 |

        ## <script>x</script>
        """;

    private static async Task<(HttpStatusCode Status, string Body)> Get(WebHarness web, string url)
    {
        var response = await web.Client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // 1113-C: every heading carries the anchor PlanSteps gives it (repeats get -2), so #anchor targets exist.
    [Fact]
    public async Task Plan_EveryHeadingCarriesItsPlanStepsAnchor()
    {
        using var web = new WebHarness();
        web.Data.Plans[Path] = Plan;

        var (status, html) = await Get(web, "/plan?path=" + Uri.EscapeDataString(Path));

        Assert.Equal(HttpStatusCode.OK, status);
        var anchors = PlanSteps.Headings(Plan).Select(h => h.Anchor).ToList();
        Assert.Contains("first-step-2", anchors);
        foreach (var anchor in anchors)
        {
            Assert.Contains(" id='" + anchor + "'", html);
        }

        Assert.Contains("<h2 id='first-step'>First step</h2>", html);
        Assert.Contains("<h3 id='part-a'>Part A</h3>", html);
        Assert.Contains("<h1 id='the-plan'>The plan</h1>", html);
    }

    // The same markdown rendering: lists, tables, fenced code, inline code, and nothing in the text becomes markup.
    [Fact]
    public async Task Plan_RendersListsTablesAndCodeAndEncodesText()
    {
        using var web = new WebHarness();
        web.Data.Plans[Path] = Plan;

        var (_, html) = await Get(web, "/plan?path=" + Uri.EscapeDataString(Path));

        Assert.Contains("<ul><li>one</li><li>two</li></ul>", html);
        Assert.Contains("<th>Name</th><th>Value</th>", html);
        Assert.Contains("<td>a</td><td>1</td>", html);
        Assert.Contains("<pre>## not a heading\n</pre>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.DoesNotContain("<script>x", html);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;", html);
        Assert.DoesNotContain("not-a-heading", html);
    }

    // 1113-C: an unknown path is a plain not-found page.
    [Fact]
    public async Task Plan_UnknownPath_IsNotFound()
    {
        using var web = new WebHarness();

        var (status, body) = await Get(web, "/plan?path=docs%2Fai%2Fnone.md");
        var (noPath, _) = await Get(web, "/plan");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("not found", body);
        Assert.DoesNotContain("<html", body);
        Assert.Equal(HttpStatusCode.NotFound, noPath);
    }

    // 1113-C: /card and /review take the short id as id= as well as the full name.
    [Fact]
    public async Task CardAndReview_AcceptIdQuery()
    {
        using var web = new WebHarness();
        web.Data.Cards["1091-C"] = "# 1091-C: a done card";
        web.Data.Reviews["1091-C"] = new Scheduler.Core.WebContract.ReviewPanel { Name = "1091-C-x.md", Title = "1091-C: x", Status = "done" };

        var (cardStatus, card) = await Get(web, "/card?id=1091-C");
        var (reviewStatus, review) = await Get(web, "/review?id=1091-C");

        Assert.Equal(HttpStatusCode.OK, cardStatus);
        Assert.Contains("a done card", card);
        Assert.Equal(HttpStatusCode.OK, reviewStatus);
        Assert.Contains("1091-C: x", review);
    }
}
