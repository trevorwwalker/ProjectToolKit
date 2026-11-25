// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Scheduler.Core.Views;

/// <summary>One step of a plan document: a <c>##</c> heading (n) or a <c>###</c> heading under one (n.m).</summary>
public sealed record PlanStep
{
    /// <summary>The step number n.</summary>
    public required int Number { get; init; }

    /// <summary>The sub-step number m, 0 for a top-level step.</summary>
    public int Sub { get; init; }

    public required string Title { get; init; }

    /// <summary>The heading's anchor in the rendered page.</summary>
    public required string Anchor { get; init; }

    public IReadOnlyList<PlanStep> SubSteps { get; init; } = [];

    /// <summary>"n" or "n.m".</summary>
    public string Label => Sub == 0
        ? Number.ToString(CultureInfo.InvariantCulture)
        : Number.ToString(CultureInfo.InvariantCulture) + "." + Sub.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The steps of a plan document, from its headings; and the anchors the rendered page puts on every heading.</summary>
public static class PlanSteps
{
    private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"^ {0,3}(```|~~~)", RegexOptions.Compiled);
    private static readonly Regex LeadingNumber = new(@"^(\d+)(?:\.(\d+))?[.):]?(?=\s|$)", RegexOptions.Compiled);

    /// <summary>One heading of a document with its level (1 to 6), plain title and unique anchor.</summary>
    public readonly record struct HeadingInfo(int Level, string Title, string Anchor);

    /// <summary>Every heading of the text outside code fences, in order, with its anchor (a repeat gets -2, -3 ...).</summary>
    public static IReadOnlyList<HeadingInfo> Headings(string? text)
    {
        var result = new List<HeadingInfo>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fence = false;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Fence.IsMatch(line))
            {
                fence = !fence;
                continue;
            }

            if (fence)
            {
                continue;
            }

            var m = Heading.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var title = Plain(m.Groups[2].Value);
            var slug = Slug(title);
            var n = seen.GetValueOrDefault(slug) + 1;
            seen[slug] = n;
            result.Add(new HeadingInfo(m.Groups[1].Length, title, n == 1 ? slug : slug + "-" + n.ToString(CultureInfo.InvariantCulture)));
        }

        return result;
    }

    /// <summary>
    /// The steps of a plan document (VW-24). A <c>##</c> heading is numbered when its title starts "n." or "n)" with no
    /// sub-number. In a plan with a numbered <c>##</c> heading only those are steps (each keeps its n, and the <c>###</c>
    /// headings under it are n.m); an unnumbered <c>##</c> and the <c>###</c> under it are not steps. A plan with none
    /// keeps the older rule: each <c>##</c> in order is step n, each <c>###</c> under it n.m.
    /// </summary>
    public static IReadOnlyList<PlanStep> Of(string? text)
    {
        var steps = new List<PlanStep>();
        List<PlanStep> subs = [];
        int number = 0, sub = 0;
        PlanStep? current = null;

        void Close()
        {
            if (current is not null)
            {
                steps.Add(current with { SubSteps = subs });
            }
        }

        var headings = Headings(text);
        var numberedPlan = headings.Any(h => h.Level == 2 && IsNumbered(h.Title));

        foreach (var h in headings)
        {
            if (h.Level == 2)
            {
                Close();
                current = null;
                if (numberedPlan && !IsNumbered(h.Title))
                {
                    continue;
                }

                var lead = LeadingNumber.Match(h.Title);
                number = lead.Success && !lead.Groups[2].Success ? Parse(lead.Groups[1].Value) : number + 1;
                sub = 0;
                subs = [];
                current = new PlanStep { Number = number, Title = h.Title, Anchor = h.Anchor };
            }
            else if (h.Level == 3 && current is not null)
            {
                var lead = LeadingNumber.Match(h.Title);
                sub = lead.Success && lead.Groups[2].Success && Parse(lead.Groups[1].Value) == number
                    ? Parse(lead.Groups[2].Value)
                    : sub + 1;
                subs.Add(new PlanStep { Number = number, Sub = sub, Title = h.Title, Anchor = h.Anchor });
            }
        }

        Close();
        return steps;
    }

    /// <summary>A GitHub-style slug: lower case, spaces to -, everything but letters, digits, - and _ dropped.</summary>
    public static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (c == ' ' || c == '\t')
            {
                sb.Append('-');
            }
            else if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>The heading's text as the page shows it: links, code marks and emphasis reduced to their words.</summary>
    private static string Plain(string s)
    {
        s = Regex.Replace(s, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
        s = s.Replace("`", "").Replace("**", "").Replace("__", "");
        s = Regex.Replace(s, @"(?<![\w*])\*([^*]+)\*(?![\w*])", "$1");
        return s.Trim();
    }

    private static bool IsNumbered(string title)
    {
        var lead = LeadingNumber.Match(title);
        return lead.Success && !lead.Groups[2].Success;
    }

    private static int Parse(string digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
}
