using System.Text.RegularExpressions;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;

namespace Scheduler.Host.PageActions;

/// <summary>
/// 947-B: a worker type's name is what a card's <c>Worker:</c> line names (WT-3), so renaming a type
/// has to rename the cards that name it - otherwise the card fits no type and waits in <c>queue/</c>
/// forever. This is the card half of that: the open cards that name a name, and a card's text with its
/// <c>Worker:</c> line changed and nothing else touched.
/// A card names a type when its header <c>Worker:</c> value, read as CARD-10 reads it, is the name
/// (case-insensitive, whole value only). A line that names a model, or one below the header block
/// (760-A), is not a reference to a type and is left alone.
/// </summary>
public static partial class CardWorkerLine
{
    /// <summary>One open card and main's text of it.</summary>
    public sealed record OpenCard(Card Card, string Text);

    /// <summary>
    /// The open cards on main - <c>queue/</c>, <c>active/</c> and <c>review/</c>; a <c>done/</c> card
    /// is not opened (CARD-5) and is not a reference that can strand - with the text they have now.
    /// Nothing from the card cache is used: a rename is measured against main, which is what the
    /// commit that follows is made against.
    /// </summary>
    public static IReadOnlyList<OpenCard> Open(IMainRepository main, string jobsDir)
    {
        var tree = main.ListTree(jobsDir);
        if (tree is null)
        {
            return [];
        }

        var cards = new List<OpenCard>();
        foreach (var entry in tree)
        {
            var card = CardFiles.FromPath(jobsDir, entry.Path);
            if (card is null || card.Folder == CardFolder.Done)
            {
                continue;
            }

            var text = main.ReadFile(entry.Path);
            if (text is null)
            {
                continue;   // unreadable now: not rewritten, and not counted as a reference
            }

            cards.Add(new OpenCard(CardFiles.ParseText(card, text), text));
        }

        return cards;
    }

    /// <summary>
    /// 1064-B (state-cache.md MM-4, MM-14): the same open cards from the repository's model of main - parsed as the
    /// CLI read parses them, their text read in process at the model's commit - so a rename or a removal checks the
    /// cards with no git process. A card whose text cannot be read is left out, as above.
    /// </summary>
    public static IReadOnlyList<OpenCard> Open(IMainModel model, string jobsDir)
    {
        var cards = new List<OpenCard>();
        foreach (var card in model.Cards(jobsDir))
        {
            if (card.Folder == CardFolder.Done || model.Text(card.Path) is not { } text)
            {
                continue;
            }

            cards.Add(new OpenCard(card, text));
        }

        return cards;
    }

    /// <summary>
    /// The open cards whose <c>Worker:</c> line names <paramref name="name"/>, in the order queue/,
    /// active/, review/ and then by number, so a message that lists them reads as the queue does.
    /// </summary>
    public static IReadOnlyList<OpenCard> Naming(IReadOnlyList<OpenCard> cards, string name) =>
        [.. cards
            .Where(c => c.Card.WorkerReq is { Length: > 0 } req && Text.Same(req.Trim(), name))
            .OrderBy(c => c.Card.Folder)
            .ThenBy(c => c.Card.Num)
            .ThenBy(c => c.Card.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// <paramref name="text"/> with its header <c>Worker:</c> value changed from
    /// <paramref name="oldName"/> to <paramref name="newName"/>, or null when the card does not name
    /// <paramref name="oldName"/>. Only that line is rewritten: the field's leading <c>Worker:</c> and
    /// spacing, any <c>(reason)</c> after the value, the other lines and the file's own line endings
    /// are kept as they were.
    /// </summary>
    public static string? Rename(string text, string oldName, string newName)
    {
        if (oldName.Length == 0 || newName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return null;
        }

        var lines = text.Split('\n');
        var hit = -1;
        var inHeader = true;
        for (var n = 0; n < lines.Length; n++)
        {
            // 760-A: the fields are the header block only, so a "Worker: x" sentence in the Task is
            // not the card's Worker: line and must not be rewritten.
            if (HeaderEnd().IsMatch(lines[n]))
            {
                inHeader = false;
                continue;
            }

            if (inHeader && ValueOf(lines[n]) is { } value && Text.Same(value, oldName))
            {
                hit = n;   // CARD-6..11: the last of a field in the header is the one that counts
            }
        }

        if (hit < 0)
        {
            return null;
        }

        // Only the value changes: the field's name, its spacing, any "(reason)" after the value, the
        // rest of the lines and the file's own line endings are put back exactly as they were read.
        var line = lines[hit];
        var after = line[Field.Length..];
        var body = after.TrimStart();
        var core = (body.IndexOf('(') is var paren and >= 0 ? body[..paren] : body).TrimEnd();
        lines[hit] = line[..(line.Length - body.Length)] + newName + body[core.Length..];
        return string.Join("\n", lines);
    }

    // The card's header Worker: value as CARD-10 reads it - the text after "Worker:", with the
    // surrounding whitespace and any "(reason)" after it taken off - or null when the line is no such
    // field. Only ever called on a header line, which is why "the last one wins" is the caller's job.
    private static string? ValueOf(string line)
    {
        if (!line.StartsWith(Field, StringComparison.Ordinal))
        {
            return null;
        }

        var body = line[Field.Length..].TrimStart();
        if (body.Length == 0)
        {
            return null;
        }

        var value = (body.IndexOf('(') is var paren and >= 0 ? body[..paren] : body).Trim();
        return value.Length > 0 ? value : null;
    }

    // The field CARD-10 reads, matched as CardFiles matches it: the line starts with it, at the
    // margin, in the header block.
    private const string Field = "Worker:";

    // The line that ends the header block (760-A).
    [GeneratedRegex(@"^## ")]
    private static partial Regex HeaderEnd();
}

/// <summary>
/// 1064-B (state-cache.md MM-14): a read of main by the page and its actions. Once the repository's model of main has
/// loaded it answers, in process; until then (or for a repository the model does not follow) the git CLI reader
/// answers as before (MM-16).
/// </summary>
public static class MainRead
{
    /// <summary>The model when it has loaded, else null.</summary>
    public static IMainModel? Model(IMainFeed? feed) => feed?.Model is { Snapshot: not null } model ? model : null;

    /// <summary>The model after a ref read (MM-8: in process, no git), for a read the commit that follows is made against.</summary>
    public static IMainModel? Fresh(IMainFeed? feed)
    {
        if (Model(feed) is null)
        {
            return null;
        }

        feed!.Tick();
        return Model(feed);
    }

    /// <summary>The text of <paramref name="path"/> on main: from <paramref name="model"/> when it is there, else from <paramref name="main"/>.</summary>
    public static string? Text(IMainModel? model, IMainRepository main, string path) =>
        model is not null ? model.Text(path) : main.ReadFile(path);

    /// <summary>The text of <paramref name="path"/> on main, from the model once it has loaded, else through the CLI.</summary>
    public static string? Text(IMainFeed? feed, IMainRepository main, string path) => Text(Model(feed), main, path);
}
