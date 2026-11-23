using System.Net;

namespace Scheduler.Web.Views;

/// <summary>
/// The project chip of MP-P9 (developer, 2026-11-20): in the style of the category chip (970-C), the
/// project's short name in a colour of its own - stable per project, white text readable in every
/// theme, from a palette apart from the category colours - and the full name on hover. It is drawn
/// only while the pool has two or more projects; the page hides it while the dropdown is on one
/// project (the <c>projchip</c> rule in <see cref="CombinedPageRenderer"/>'s script).
/// </summary>
public static class ProjectChip
{
    /// <summary>The longest name shown whole; a longer one is cut with an ellipsis.</summary>
    public const int MaxName = 12;

    /// <summary>The project colours: dark enough for white text (4.5:1), none of them a category colour.</summary>
    public static readonly IReadOnlyList<string> Palette =
        ["#334155", "#9a3412", "#065f46", "#3730a3", "#831843", "#854d0e", "#155e75", "#5b21b6"];

    /// <summary>
    /// The chip for <paramref name="project"/>, or "" while <paramref name="projects"/> has fewer than two.
    /// </summary>
    /// <param name="project">The project the row belongs to.</param>
    /// <param name="projects">Every project in the pool, in the pool file's order.</param>
    public static string Html(string project, IReadOnlyList<string> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        if (projects.Count < 2 || project.Length == 0)
        {
            return "";
        }

        var shown = project.Length > MaxName ? project[..(MaxName - 1)] + "…" : project;
        return $"<span class='chip projchip' style='background:{Colour(project, projects)};color:#ffffff' title='{WebUtility.HtmlEncode(project)}'>{WebUtility.HtmlEncode(shown)}</span>";
    }

    /// <summary>
    /// A project's colour: picked by its name, so it stays the same however the pool file is ordered;
    /// when an earlier project already holds that colour, the next free one.
    /// </summary>
    public static string Colour(string project, IReadOnlyList<string> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var taken = new HashSet<int>();
        foreach (var name in projects)
        {
            var index = Free(Hash(name), taken);
            if (string.Equals(name, project, StringComparison.OrdinalIgnoreCase))
            {
                return Palette[index];
            }

            taken.Add(index);
        }

        return Palette[Hash(project)];
    }

    private static int Free(int start, HashSet<int> taken)
    {
        for (var i = 0; i < Palette.Count; i++)
        {
            var index = (start + i) % Palette.Count;
            if (!taken.Contains(index))
            {
                return index;
            }
        }

        return start;
    }

    // FNV-1a over the lower-cased name: the same project gets the same colour in every run.
    private static int Hash(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name.ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash % (uint)Palette.Count);
    }
}
