// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;

namespace Scheduler.Core.Contracts;

/// <summary>
/// Text compares as PowerShell does them (porting note G-1): <c>-eq</c>, <c>-match</c>,
/// <c>-like</c> and hashtable keys are case-insensitive, and <c>Sort-Object</c> uses a
/// case-insensitive culture compare. Every rule that compares text uses these unless the spec
/// says it is case-sensitive.
/// </summary>
public static class Text
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Equality for <c>-eq</c>/<c>-ne</c> and keys (<c>Contains</c> on a hashtable).</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Order for <c>Sort-Object</c> and <c>-lt</c>/<c>-ge</c>: culture compare, ignoring case.</summary>
    public static StringComparer SortComparer { get; } = StringComparer.CurrentCultureIgnoreCase;

    /// <summary><c>a -eq b</c> on text.</summary>
    public static bool Same(string? a, string? b) => Comparer.Equals(a, b);

    /// <summary><c>input -match pattern</c>, with the match (groups are <c>$Matches</c>).</summary>
    public static Match Match(string input, string pattern) => Regex.Match(input, pattern, Options);

    /// <summary><c>input -match pattern</c>.</summary>
    public static bool IsMatch(string input, string pattern) => Regex.IsMatch(input, pattern, Options);

    /// <summary>
    /// <c>input -like pattern</c>: the whole text against a wildcard, where <c>*</c> is any run,
    /// <c>?</c> one character and <c>[...]</c> a character set.
    /// </summary>
    public static bool Like(string input, string pattern) =>
        Regex.IsMatch(input, WildcardToRegex(pattern), Options | RegexOptions.Singleline);

    private static string WildcardToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                case '`' when i + 1 < pattern.Length: sb.Append(Regex.Escape(pattern[++i].ToString())); break;
                case '[':
                    var end = pattern.IndexOf(']', i + 1);
                    if (end < 0) { sb.Append(@"\["); break; }
                    sb.Append('[').Append(pattern[(i + 1)..end].Replace(@"\", @"\\")).Append(']');
                    i = end;
                    break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        return sb.Append('$').ToString();
    }
}
