// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Scheduler.Core.Views;

namespace Scheduler.Web.Views;

/// <summary>
/// A small Markdown to HTML for a card's Result (roadmap item 6, card 650-C): headings, bullet and
/// numbered lists, fenced code, paragraphs, inline code and bold. Text is HTML-encoded first, so
/// nothing in a card can add markup.
/// </summary>
public static partial class ReviewMarkdown
{
    /// <summary>The HTML for <paramref name="markdown"/>.</summary>
    public static string ToHtml(string markdown) => ToHtml(markdown, anchors: false);

    /// <summary>
    /// The HTML for <paramref name="markdown"/>. With <paramref name="anchors"/> (a plan document, 1113-C) a heading
    /// of level n is an <c>h</c>n carrying <c>id</c> = the anchor <see cref="PlanSteps.Headings"/> gives it, so a
    /// link <c>#anchor</c> opens the page at that section; otherwise (a card's Result) a heading is two levels
    /// down and has no id.
    /// </summary>
    public static string ToHtml(string markdown, bool anchors)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var ids = anchors ? PlanSteps.Headings(markdown).Select(h => h.Anchor).ToList() : [];
        var nextId = 0;
        var table = new List<string>();
        var sb = new StringBuilder();
        var list = "";
        var para = new List<string>();
        var fenced = false;

        void EndPara()
        {
            if (para.Count > 0)
            {
                sb.Append("<p>").Append(string.Join(' ', para.Select(Inline))).Append("</p>");
                para.Clear();
            }
        }

        void EndTable()
        {
            if (table.Count > 0)
            {
                Table(sb, table);
                table.Clear();
            }
        }

        void EndList()
        {
            if (list.Length > 0)
            {
                sb.Append("</").Append(list).Append('>');
                list = "";
            }
        }

        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                EndPara();
                EndList();
                EndTable();
                sb.Append(fenced ? "</pre>" : "<pre>");
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                sb.Append(WebUtility.HtmlEncode(raw)).Append('\n');
                continue;
            }

            var line = raw.Trim();
            var heading = HeadingLine().Match(line);
            var item = ItemLine().Match(line);
            if (line.StartsWith('|') && anchors)
            {
                EndPara();
                EndList();
                table.Add(line);
                continue;
            }

            EndTable();
            if (line.Length == 0)
            {
                EndPara();
                EndList();
            }
            else if (heading.Success)
            {
                EndPara();
                EndList();
                var level = anchors ? heading.Groups[1].Length : Math.Min(heading.Groups[1].Length + 2, 6);
                var id = anchors && nextId < ids.Count ? " id='" + WebUtility.HtmlEncode(ids[nextId++]) + "'" : "";
                sb.Append("<h").Append(level).Append(id).Append('>').Append(Inline(heading.Groups[2].Value)).Append("</h").Append(level).Append('>');
            }
            else if (item.Success)
            {
                EndPara();
                var kind = char.IsDigit(item.Groups[1].Value[0]) ? "ol" : "ul";
                if (list != kind)
                {
                    EndList();
                    sb.Append('<').Append(kind).Append('>');
                    list = kind;
                }

                sb.Append("<li>").Append(Inline(item.Groups[2].Value)).Append("</li>");
            }
            else
            {
                EndList();
                para.Add(line);
            }
        }

        EndPara();
        EndList();
        EndTable();
        if (fenced)
        {
            sb.Append("</pre>");
        }

        return sb.ToString();
    }

    // A pipe table: a header row, a |---| row, then the body rows (anything else is plain rows).
    private static void Table(StringBuilder sb, List<string> rows)
    {
        var header = rows.Count > 1 && TableRule().IsMatch(rows[1]);
        sb.Append("<table>");
        for (var i = 0; i < rows.Count; i++)
        {
            if (header && i == 1)
            {
                continue;
            }

            var tag = header && i == 0 ? "th" : "td";
            sb.Append("<tr>");
            foreach (var cell in rows[i].Trim().Trim('|').Split('|'))
            {
                sb.Append('<').Append(tag).Append('>').Append(Inline(cell.Trim())).Append("</").Append(tag).Append('>');
            }

            sb.Append("</tr>");
        }

        sb.Append("</table>");
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = CodeSpan().Replace(encoded, "<code>$1</code>");
        return BoldSpan().Replace(encoded, "<b>$1</b>");
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")]
    private static partial Regex TableRule();

    [GeneratedRegex(@"^([-*]|\d+\.)\s+(.*)$")]
    private static partial Regex ItemLine();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldSpan();
}
