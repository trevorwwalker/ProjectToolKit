// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// Whether a health check is running, read off the health line the host writes (HLTH-5): while a
/// check runs its line ends <c>"; checking &lt;sha&gt; (n min)"</c>, and nothing else the host
/// writes looks like that. The Control window's <b>Check main</b> button (card 930-C) and the live
/// feed both read it here, so the button and the title bar always show the same live state.
/// </summary>
public static class HealthChecking
{
    /// <summary>What the host's running-check clause says before the sha it started on.</summary>
    private const string Marker = "checking ";

    /// <summary>The sha a running check started on, or "" when no health check is running.</summary>
    public static string Sha(PageHealthView health)
    {
        ArgumentNullException.ThrowIfNull(health);

        // The host sets the field; the text parse below is only for a view that leaves it empty.
        if (!string.IsNullOrEmpty(health.CheckingSha))
        {
            return health.CheckingSha;
        }

        var text = health.Text ?? "";

        for (var at = text.IndexOf(Marker, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(Marker, at + Marker.Length, StringComparison.OrdinalIgnoreCase))
        {
            var start = at + Marker.Length;
            var end = start;
            while (end < text.Length && IsHex(text[end]))
            {
                end++;
            }

            // A sha is at least four hex digits and the host follows it with " (n min)".
            if (end - start >= 4 && (end == text.Length || text[end] is ' ' or '('))
            {
                return text[start..end];
            }
        }

        return "";
    }

    private static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
}
