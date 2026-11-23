using Scheduler.Core.Contracts;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The open cards a category's letter is in, which keep the Categories panel from removing that category
/// or changing its letter (992-B). A card uses a letter through its file name (the id) or its header's
/// <c>Category:</c> (or the older <c>Lane:</c>) line (969-A).
/// </summary>
public static class CardCategoryLine
{
    /// <summary>The open cards that use <paramref name="letter"/>, in folder and number order.</summary>
    public static IReadOnlyList<CardWorkerLine.OpenCard> Using(IReadOnlyList<CardWorkerLine.OpenCard> cards, string letter) =>
        [.. cards
            .Where(c => Text.Same(c.Card.Category, letter) || (c.Card.HeaderCategory is { } header && Text.Same(header.Trim(), letter)))
            .OrderBy(c => c.Card.Folder)
            .ThenBy(c => c.Card.Num)
            .ThenBy(c => c.Card.Name, StringComparer.OrdinalIgnoreCase)];
}
