// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;

namespace Scheduler.Core.Views;

/// <summary>VW-13: a plan's status line.</summary>
public static class PlanStatus
{
    public const int Length = 200;
    public const string None = "no status line";

    private static readonly Regex StatusLine = new(@"^\s*(\*{0,2}Status\b.*)$", RegexOptions.IgnoreCase);

    /// <summary>The first line starting with <c>Status:</c>, <c>*Status</c> or <c>**Status</c>, cut at 200 characters.</summary>
    public static string Of(string? text)
    {
        foreach (var line in Lines(text))
        {
            var m = StatusLine.Match(line);
            if (m.Success)
            {
                return Cut(m.Groups[1].Value.Trim());
            }
        }

        return None;
    }

    private static string Cut(string s) => s.Length <= Length ? s : s[..Length];

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Split('\n');
}
