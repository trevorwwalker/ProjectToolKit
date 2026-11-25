// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Net;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The coloured chip the page shows wherever it names a category (970-C): the category's short name
/// (cut at <see cref="MaxName"/> characters with an ellipsis), its full purpose as the tooltip, and
/// a colour that is the same on every render. The colour is the one the categories file gives, else
/// <see cref="Palette"/> indexed by the category's order in the file. The chip carries its own
/// background and text colour, so it reads the same in every theme.
/// </summary>
public static class CategoryChip
{
    /// <summary>Names longer than this are cut with an ellipsis.</summary>
    public const int MaxName = 10;

    /// <summary>
    /// Fixed colours by category order. Each is dark enough for white text at contrast 4.5:1 or more
    /// (checked in the tests), so the chip needs no theme.
    /// </summary>
    public static readonly IReadOnlyList<string> Palette =
        ["#1d4ed8", "#047857", "#b45309", "#7e22ce", "#be123c", "#0e7490", "#4d7c0f", "#a21caf"];

    /// <summary>The chip for <paramref name="letter"/>, or empty when the letter is empty.</summary>
    public static string Html(IReadOnlyList<PageCategory> categories, string letter)
    {
        if (letter.Length == 0)
        {
            return "";
        }

        var index = -1;
        for (var i = 0; i < categories.Count; i++)
        {
            if (string.Equals(categories[i].Letter, letter, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var category = index >= 0 ? categories[index] : null;
        var name = category is { Name.Length: > 0 } ? category.Name : letter;
        var shown = name.Length > MaxName ? name[..(MaxName - 1)] + "…" : name;
        var background = category is { Color.Length: > 0 } && IsHex(category.Color)
            ? category.Color
            : Palette[Math.Max(index, 0) % Palette.Count];
        var text = ContrastWithWhite(background) >= 4.5 ? "#ffffff" : "#000000";
        var tip = category is { Purpose.Length: > 0 } ? category.Purpose : $"Category {letter}";
        return $"<span class='chip' style='background:{background};color:{text}' title='{WebUtility.HtmlEncode(tip)}'>{WebUtility.HtmlEncode(shown)}</span>";
    }

    /// <summary>The WCAG contrast ratio of white text on <paramref name="hex"/>.</summary>
    public static double ContrastWithWhite(string hex) => 1.05 / (Luminance(hex) + 0.05);

    private static bool IsHex(string s) =>
        s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

    private static double Luminance(string hex)
    {
        double Channel(int at)
        {
            var c = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
