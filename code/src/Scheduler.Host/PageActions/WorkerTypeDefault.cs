using Scheduler.Core.Contracts;

namespace Scheduler.Host.PageActions;

/// <summary>
/// 1146-A: the default worker type a Worker types Save writes (WT-3): the type a card with no
/// <c>Worker:</c> line goes to.
/// </summary>
public static class WorkerTypeDefault
{
    /// <summary>
    /// The saved rows' default: <paramref name="requested"/> when it names a row (by its new name);
    /// with none requested, the row that was <paramref name="current"/> (following a rename through the
    /// row's original name), else the first row. Null with <paramref name="refusal"/> set when
    /// <paramref name="requested"/> names no row.
    /// </summary>
    public static string? Choose(
        IReadOnlyList<(string Name, string Original)> rows, string current, string requested, out string refusal)
    {
        refusal = "";
        var wanted = (requested ?? "").Trim();
        if (wanted.Length > 0)
        {
            var named = rows.FirstOrDefault(r => Text.Same(r.Name, wanted)).Name;
            if (named is null)
            {
                refusal = $"worker types not saved: the default worker type {wanted} is not one of the types";
            }

            return named;
        }

        return rows.FirstOrDefault(r => current.Length > 0 && Text.Same(r.Original, current)).Name
            ?? rows.FirstOrDefault().Name
            ?? "";
    }
}
