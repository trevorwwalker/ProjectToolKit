// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>A card that asks for a number: <c>{jobsDir}/{folder}/new-{L}-{slug}.md</c> (1111-B).</summary>
/// <param name="Path">Its path in the repository.</param>
/// <param name="Category">Its category letter, upper case.</param>
/// <param name="Slug">The rest of its name.</param>
public sealed record NewCard(string Path, string Category, string Slug)
{
    /// <summary><c>new-{L}-{slug}</c>, the name references use.</summary>
    public string Name => $"new-{Category}-{Slug}";
}

/// <summary>A new card and the number it was given.</summary>
/// <param name="Card">The card as it asked.</param>
/// <param name="Num">Its number.</param>
/// <param name="To">Its path once numbered.</param>
public sealed record NumberedCard(NewCard Card, long Num, string To)
{
    /// <summary><c>{n}-{L}</c>, the card's id.</summary>
    public string Id => $"{Num.ToString(CultureInfo.InvariantCulture)}-{Card.Category}";

    /// <summary><c>{n}-{L}-{slug}</c>, its file name without <c>.md</c>.</summary>
    public string Name => $"{Id}-{Card.Slug}";
}

/// <summary>What numbering gave: the files renamed and rewritten, each number, and the <c>new-*</c> names left as they were.</summary>
/// <param name="Numbered">The cards numbered, in order.</param>
/// <param name="Texts">Every file whose text changed, keyed by where it ends up.</param>
/// <param name="Unknown">The <c>new-{L}-{slug}</c> names the texts still mention: not in this batch, so left as they are.</param>
public sealed record NumberedFiles(IReadOnlyList<NumberedCard> Numbered, IReadOnlyDictionary<string, string> Texts, IReadOnlyList<string> Unknown)
{
    /// <summary>Where a path ends up: a numbered card's new path, else itself.</summary>
    public string Where(string path) =>
        Numbered.FirstOrDefault(n => string.Equals(n.Card.Path, path, StringComparison.Ordinal))?.To ?? path;
}

/// <summary>
/// 1111-B (planner-outbox.md "Card numbers"): only the scheduler numbers cards. A writer names a new card
/// <c>new-{L}-{slug}.md</c>; the scheduler gives it the next number of the pool's one counter, renames it
/// <c>{n}-{L}-{slug}.md</c>, fixes its title line and rewrites the references to it in the same batch or
/// commit. Pure: the counter is the caller's.
/// </summary>
public static class CardNumbers
{
    private const string NameChars = "A-Za-z0-9_.-";

    /// <summary>The new card at <paramref name="path"/>, or null when the path is not one (queue, active, review or done).</summary>
    public static NewCard? NewFromPath(string jobsDir, string path)
    {
        var m = Regex.Match(path, @"^" + Regex.Escape(jobsDir.Trim('/')) + @"/(?:queue|active|review|done)/[Nn][Ee][Ww]-([A-Za-z])-([^/]+)\.md$");
        return m.Success ? new NewCard(path, m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value) : null;
    }

    /// <summary>The highest card number among <paramref name="paths"/> in any folder of <paramref name="jobsDir"/> (archive too); 0 when none.</summary>
    public static long Highest(string jobsDir, IEnumerable<string> paths)
    {
        var pattern = new Regex(@"^" + Regex.Escape(jobsDir.Trim('/')) + @"/(?:queue|active|review|done|archive)/(\d+)-[A-Za-z]-[^/]+\.md$");
        long high = 0;
        foreach (var p in paths)
        {
            var m = pattern.Match(p);
            if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > high)
            {
                high = n;
            }
        }

        return high;
    }

    /// <summary>
    /// Numbers every new card among <paramref name="texts"/>' paths, in their order, with <paramref name="next"/>,
    /// and rewrites the references in all of them: <c>new-{L}-{slug}.md</c> becomes <c>{n}-{L}-{slug}.md</c>, a bare
    /// <c>new-{L}-{slug}</c> becomes <c>{n}-{L}</c>, and a card's own title line <c># new-{L}: ...</c> becomes
    /// <c># {n}-{L}: ...</c>. <see cref="NumberedFiles.Texts"/> holds the new cards and every other file that changed.
    /// </summary>
    /// <param name="jobsDir">The jobs folder.</param>
    /// <param name="texts">The files of the batch or commit, path to text, in order.</param>
    /// <param name="next">The next number of the counter.</param>
    public static NumberedFiles Number(string jobsDir, IEnumerable<KeyValuePair<string, string>> texts, Func<long> next)
    {
        var files = texts.ToList();
        var numbered = new List<NumberedCard>();
        foreach (var (path, _) in files)
        {
            if (NewFromPath(jobsDir, path) is { } card && !numbered.Any(n => Text.Same(n.Card.Name, card.Name)))
            {
                var num = next();
                var dir = path[..(path.LastIndexOf('/') + 1)];
                numbered.Add(new NumberedCard(card, num, $"{dir}{num.ToString(CultureInfo.InvariantCulture)}-{card.Category}-{card.Slug}.md"));
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var (path, text) in files)
        {
            var own = numbered.FirstOrDefault(n => string.Equals(n.Card.Path, path, StringComparison.Ordinal));
            var rewritten = Rewrite(text, numbered, own);
            foreach (var name in Mentions(rewritten).Where(name => !unknown.Contains(name, StringComparer.Ordinal)))
            {
                unknown.Add(name);
            }

            if (own is not null || !string.Equals(rewritten, text, StringComparison.Ordinal))
            {
                result[own?.To ?? path] = rewritten;
            }
        }

        return new NumberedFiles(numbered, result, unknown);
    }

    /// <summary>
    /// A planner batch's change with its new cards numbered (1111-B (a)): each new card is written at its numbered
    /// path instead, and every written file's references are rewritten. A change without new cards comes back as it is.
    /// </summary>
    public static (JobsChange Change, NumberedFiles Numbering) Number(JobsChange change, string jobsDir, Func<long> next)
    {
        var numbering = Number(jobsDir, change.Writes, next);
        if (numbering.Numbered.Count == 0 && numbering.Texts.Count == 0)
        {
            return (change, numbering);
        }

        var writes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in change.Writes)
        {
            var to = numbering.Where(path);
            writes[to] = numbering.Texts.TryGetValue(to, out var t) ? t : text;
        }

        return (change with { Writes = writes }, numbering);
    }

    /// <summary>
    /// The commit that numbers new cards found on main (1111-B (b)): each is moved to its numbered path and written
    /// there with its title and references fixed, and any of <paramref name="others"/> that names one is rewritten.
    /// Message: <c>jobs: number new-{L}-{slug} as {n}</c>, the names joined with ", " when there are several.
    /// </summary>
    /// <param name="jobsDir">The jobs folder.</param>
    /// <param name="cards">The new cards on main, path to text.</param>
    /// <param name="others">The other files whose references are rewritten (the queue's cards), path to text.</param>
    /// <param name="next">The next number of the counter.</param>
    public static (JobsChange Change, NumberedFiles Numbering) OnMain(string jobsDir, IEnumerable<KeyValuePair<string, string>> cards,
        IEnumerable<KeyValuePair<string, string>> others, Func<long> next)
    {
        var numbering = Number(jobsDir, cards.Concat(others), next);
        var moves = numbering.Numbered.Select(n => new FileMove { From = n.Card.Path, To = n.To }).ToList();
        var message = "jobs: number " + string.Join(", ", numbering.Numbered.Select(n => $"{n.Card.Name} as {n.Num.ToString(CultureInfo.InvariantCulture)}"));
        return (new JobsChange { Message = message, Moves = moves, Writes = numbering.Texts }, numbering);
    }

    /// <summary>The log line for one card: <c>card numbered: {project}/new-{L}-{slug} -> {n}-{L}-{slug}</c>.</summary>
    public static string LogLine(string project, NumberedCard card) =>
        $"card numbered: {(project.Length > 0 ? project + "/" : "")}{card.Card.Name} -> {card.Name}";

    private static string Rewrite(string text, IReadOnlyList<NumberedCard> numbered, NumberedCard? own)
    {
        // Longest names first, so new-B-a never eats the start of new-B-a-b (the look-ahead also guards it).
        foreach (var n in numbered.OrderByDescending(n => n.Card.Name.Length))
        {
            text = Regex.Replace(text, @"(?<![" + NameChars + @"])" + Regex.Escape(n.Card.Name) + @"(\.md)?(?![A-Za-z0-9_-])",
                m => m.Groups[1].Success ? n.Name + ".md" : n.Id, RegexOptions.IgnoreCase);
        }

        if (own is not null)
        {
            text = Regex.Replace(text, @"\A(﻿?\s*#\s*)new-" + own.Card.Category + @"(?=[:\s])", m => m.Groups[1].Value + own.Id, RegexOptions.IgnoreCase);
        }

        return text;
    }

    private static IEnumerable<string> Mentions(string text) =>
        Regex.Matches(text, @"(?<![" + NameChars + @"])new-[A-Z]-[a-z0-9][A-Za-z0-9_-]*").Select(m => m.Value.TrimEnd('-', '.'));
}
