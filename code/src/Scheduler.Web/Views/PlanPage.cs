// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Text;
using Scheduler.Core.Views;

namespace Scheduler.Web.Views;

/// <summary>
/// The plan document <c>/plan?project=&amp;path=</c> (1113-C, views.md Plans second pass): the plan's text from main
/// as HTML in the page's theme, every heading carrying its anchor (<see cref="PlanSteps.Headings"/>), so a link
/// <c>/plan?...#anchor</c> opens it scrolled to that section.
/// </summary>
public sealed class PlanPage(PageThemes? themes = null)
{
    private PageThemes Themes { get; } = themes ?? PageThemes.BuiltIn;

    /// <summary>The page for the plan at <paramref name="path"/> whose text is <paramref name="text"/>.</summary>
    public string Render(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'>")
          .Append("<meta name='viewport' content='width=device-width, initial-scale=1'>")
          .Append(Scheduler.Web.Server.PageFavicon.Link)
          .Append("<title>").Append(WebUtility.HtmlEncode(path)).Append("</title>")
          .Append(Themes.Head())
          .Append("</head><body><div class='desk'><div class='col' id='plan'>")
          .Append("<section class='win'><div class='bar'><b>").Append(WebUtility.HtmlEncode(path))
          .Append("</b> <a href='/'>Back to the page</a></div><div class='inner plan-doc'>")
          .Append(ReviewMarkdown.ToHtml(text, anchors: true))
          .Append("</div></section></div></div></body></html>");
        return sb.ToString();
    }
}
