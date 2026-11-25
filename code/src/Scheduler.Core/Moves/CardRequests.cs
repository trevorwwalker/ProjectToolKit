// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Moves;

/// <summary>
/// Card moves asked for by request files and written only by the scheduler (card 660-B, roadmap
/// item 2; design in <c>card-moves.md</c> beside this file): the request format, their order, and
/// the <see cref="CardEdit"/> a request makes of main's text of the card. Pure; Host reads the
/// folder and commits through the answers worktree.
/// </summary>
public static class CardRequests
{
    /// <summary>The folder under the scheduler's log folder that holds the requests.</summary>
    public const string FolderName = "moves";

    /// <summary>The extension of a request ready to be read; a writer renames its <c>.tmp</c> to it.</summary>
    public const string Extension = ".move";

    /// <summary>The moves folder under <paramref name="logDir"/>.</summary>
    public static string Folder(string logDir) => System.IO.Path.Combine(logDir, FolderName);

    /// <summary>A request's file name: the time, so names sort in the order they were written, then the slot.</summary>
    public static string FileName(DateTimeOffset now, int slot) =>
        now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + $"-slot{slot}{Extension}";

    /// <summary>The order requests are applied in: by file name, ordinal (time first, then slot).</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> fileNames) =>
        [.. fileNames.Where(n => n.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];

    /// <summary>The scheduler's own claim of <paramref name="cardName"/> for the worker it starts in <paramref name="slot"/>.</summary>
    public static string ClaimText(string cardName, int slot) =>
        $"Card: {cardName}\nFrom: queue\nTo: active\nSlot: {slot}\nClaim: yes\n";

    /// <summary>
    /// Reads a request: <c>Key: value</c> header lines up to a <c>---</c> line, then the card's
    /// text (optional). <c>Card</c> and <c>To</c> are required; <c>From</c> defaults to active.
    /// </summary>
    public static CardRequest Parse(string fileName, string content)
    {
        content = (content ?? "").TrimStart('﻿');
        var lines = CardText.Split(content);
        var header = new Dictionary<string, string>(Text.Comparer);
        var i = 0;
        for (; i < lines.Count; i++)
        {
            if (lines[i].Trim() == "---")
            {
                break;
            }

            var m = Text.Match(lines[i], @"^\s*([A-Za-z]+)\s*:\s*(.*?)\s*$");
            if (m.Success)
            {
                header[m.Groups[1].Value] = m.Groups[2].Value;
            }
            else if (lines[i].Trim().Length > 0)
            {
                return CardRequest.Failed(fileName, $"not a header line: {lines[i].Trim()}");
            }
        }

        string? text = null;
        if (i < lines.Count - 1)
        {
            var body = string.Join(CardText.Eol(content), lines.Skip(i + 1));
            text = body.Trim().Length > 0 ? body : null;
        }

        var card = header.GetValueOrDefault("Card", "");
        if (card.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            card = card[..^3];
        }

        if (card.Length == 0)
        {
            return CardRequest.Failed(fileName, "no Card: line");
        }

        if (FolderOf(header.GetValueOrDefault("To", "")) is not { } to)
        {
            return CardRequest.Failed(fileName, $"{card}: To: must be queue, active, review or done");
        }

        var fromText = header.GetValueOrDefault("From", "");
        CardFolder from = CardFolder.Active;
        if (fromText.Length > 0)
        {
            if (FolderOf(fromText) is not { } f)
            {
                return CardRequest.Failed(fileName, $"{card}: From: must be queue, active, review or done");
            }

            from = f;
        }

        int? slot = int.TryParse(header.GetValueOrDefault("Slot", "").Replace("slot", "", StringComparison.OrdinalIgnoreCase), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : null;
        return new CardRequest
        {
            FileName = fileName,
            Card = card,
            From = from,
            To = to,
            Slot = slot,
            Status = header.GetValueOrDefault("Status", ""),
            Claim = Text.Same(header.GetValueOrDefault("Claim", ""), "yes"),
            Text = text,
        };
    }

    /// <summary>
    /// The edit <paramref name="request"/> makes of the card, given where it is and main's text of
    /// it. The card in <c>From</c> is moved; already in <c>To</c>, its text is changed in place,
    /// except that a claim of a card already claimed fails; anywhere else, it fails. The text is the
    /// request's (with main's answered questions and <c>## Dev</c> sections it lacks put back) or
    /// main's; what the worker's text would have lost is said in the log; Status is
    /// <c>done</c> for done/, starts with <c>review</c> for review/, else the text's or the header's.
    /// A claim adds <c>Claimed: yyyy-MM-dd, category L</c> under Status.
    /// </summary>
    public static CardEdit Plan(Card card, string mainText, CardRequest request, DateTimeOffset now)
    {
        if (!request.Ok)
        {
            return CardEdit.Failed(request.Error);
        }

        if (!Text.Same(card.Name, request.Card))
        {
            return CardEdit.Failed($"the request is for {request.Card}, not {card.Name}");
        }

        if (card.Folder != request.From)
        {
            if (card.Folder != request.To)
            {
                return CardEdit.Failed($"{card.Name} is in {Name(card.Folder)}/, not {Name(request.From)}/");
            }

            if (request.Claim)
            {
                return CardEdit.Failed($"{card.Name} is already claimed");
            }
        }

        var kept = new List<string>();
        var text = mainText;
        if (request.Text is { } t)
        {
            // 780-B (CM-4): the worker's text is the card, but main's answered questions and ## Dev
            // sections survive it.
            text = KeepDev(mainText, KeepAnswers(mainText, t, kept));
        }

        var lines = CardText.Split(text);
        var current = StatusOf(lines);
        var status = request.To switch
        {
            CardFolder.Done => "done",
            CardFolder.Review => current.StartsWith("review", StringComparison.OrdinalIgnoreCase) ? current : "review",
            _ => request.Status.Length > 0 ? request.Status : current,
        };
        if (status != current)
        {
            CardText.SetStatus(lines, status);
        }

        if (request.Claim && !lines.Any(l => Text.IsMatch(l, "^Claimed:")))
        {
            var at = lines.FindIndex(l => Text.IsMatch(l, "^Status:"));
            while (at >= 0 && at + 1 < lines.Count && Text.IsMatch(lines[at + 1], @"^\s{2,}\S"))
            {
                at++;
            }

            lines.Insert(at + 1, $"Claimed: {now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, lane {card.Category}");
        }

        var verb = request.Claim ? "start"
            : request.To == card.Folder ? "status"
            : request.To switch
            {
                CardFolder.Done => "done",
                CardFolder.Review => "review",
                CardFolder.Queue => "wait",
                _ => "resume",
            };
        var message = $"jobs: {verb} {card.Name}";
        var who = request.Claim ? "the scheduler's claim" : request.Slot is { } s ? $"slot {s}" : "a request";
        var log = $"card move from {who}: {card.Name} " +
            (request.To == card.Folder ? $"updated in {Name(card.Folder)}/" : $"{Name(card.Folder)}/ -> {Name(request.To)}/") +
            (status.Length > 0 ? $", Status: {status}" : "");
        if (kept.Count > 0)
        {
            log += $"; kept {string.Join("; ", kept)}";
        }
        var newPath = card.Path;
        if (request.To != card.Folder)
        {
            var folderDir = card.Path[..Math.Max(card.Path.LastIndexOf('/'), 0)];
            var jobsDir = folderDir[..Math.Max(folderDir.LastIndexOf('/'), 0)];
            newPath = $"{jobsDir}/{Name(request.To)}/{card.Name}.md";
        }

        return new CardEdit
        {
            CardId = card.Id,
            Path = card.Path,
            NewPath = newPath,
            Folder = request.To,
            Text = string.Join(CardText.Eol(text), lines),
            Status = status,
            Message = message.Replace('"', '\''),
            Log = log,
        };
    }

    /// <summary>
    /// The worker's text with each <c>## Dev</c> section of main's text that it lacks put back,
    /// before <c>## Result</c> (or at the end): the developer's replies are never lost.
    /// </summary>
    public static string KeepDev(string mainText, string workerText)
    {
        var eol = CardText.Eol(workerText);
        var worker = CardText.Split(workerText);
        var flat = Flat(workerText);
        var main = CardText.Split(mainText);
        var missing = new List<List<string>>();
        for (var i = 0; i < main.Count; i++)
        {
            if (!Text.IsMatch(main[i], @"^## Dev\b"))
            {
                continue;
            }

            var section = new List<string> { main[i] };
            while (i + 1 < main.Count && !Text.IsMatch(main[i + 1], "^## "))
            {
                section.Add(main[++i]);
            }

            while (section.Count > 1 && string.IsNullOrWhiteSpace(section[^1]))
            {
                section.RemoveAt(section.Count - 1);
            }

            if (!flat.Contains(Flat(string.Join("\n", section)), StringComparison.Ordinal))
            {
                missing.Add(section);
            }
        }

        if (missing.Count == 0)
        {
            return workerText;
        }

        var at = worker.FindIndex(l => Text.IsMatch(l, "^## Result"));
        var insert = missing.SelectMany(s => s.Append("")).ToList();
        if (at < 0)
        {
            worker.Add("");
            worker.AddRange(insert);
        }
        else
        {
            worker.InsertRange(at, insert);
        }

        return string.Join(eol, worker);
    }

    /// <summary>
    /// 780-B (CM-4): the worker's text with main's questions kept in it, so an answer clicked on the
    /// page while the worker ran is never lost. Each <c>- Qn:</c> line of main's <c>## Questions</c>
    /// section that is answered keeps its answered text (and its note lines): the worker's line is
    /// replaced even when it is the same question unanswered, reworded or gone. A question only main
    /// has is kept as it is. Everything else - Status, Task, Result - is the worker's. What the
    /// worker's text would have lost is added to <paramref name="kept"/>, for the scheduler log.
    /// </summary>
    public static string KeepAnswers(string mainText, string workerText, List<string>? kept = null)
    {
        var main = Questions(mainText);
        if (main.Count == 0)
        {
            return workerText;
        }

        var worker = CardText.Split(workerText);
        var replacements = new Dictionary<int, List<string>>();
        var add = new List<List<string>>();
        foreach (var (number, block, answered) in main)
        {
            var at = QuestionAt(worker, number);
            if (at < 0)
            {
                if (answered)
                {
                    kept?.Add($"Q{number} answered on main was dropped by the worker; main's line kept");
                }

                add.Add(block);
                continue;
            }

            if (!answered)
            {
                continue;
            }

            if (!Text.Same(Norm(WithoutAnswer(worker[at])), Norm(WithoutAnswer(block[0]))))
            {
                kept?.Add($"Q{number} answered on main was reworded by the worker; main's line kept");
            }

            replacements[at] = block;
        }

        if (replacements.Count == 0 && add.Count == 0)
        {
            return workerText;
        }

        var merged = new List<string>(worker.Count + add.Count);
        for (var i = 0; i < worker.Count; i++)
        {
            if (!replacements.TryGetValue(i, out var block))
            {
                merged.Add(worker[i]);
                continue;
            }

            merged.AddRange(block);
            while (i + 1 < worker.Count && Text.IsMatch(worker[i + 1], @"^\s{2,}\S"))
            {
                i++;
            }
        }

        if (add.Count > 0)
        {
            var (start, end) = QuestionsSection(merged);
            var insert = add.SelectMany(b => b.Append("")).ToList();
            if (start < 0)
            {
                var at = merged.FindIndex(l => Text.IsMatch(l, "^## Result"));
                var head = new[] { "## Questions", "" }.Concat(insert).ToList();
                if (at < 0)
                {
                    merged.Add("");
                    merged.AddRange(head);
                }
                else
                {
                    merged.InsertRange(at, head);
                }
            }
            else
            {
                var at = end;
                while (at > start && string.IsNullOrWhiteSpace(merged[at - 1]))
                {
                    at--;
                }

                merged.InsertRange(at, insert);
            }
        }

        return string.Join(CardText.Eol(workerText), merged);
    }

    /// <summary>
    /// The <c>- Qn:</c> lines of a text's first <c>## Questions</c> section, each with the indented
    /// lines under it (the developer's note), in order of the number, once each.
    /// </summary>
    private static List<(int Number, List<string> Block, bool Answered)> Questions(string text)
    {
        var lines = CardText.Split(text);
        var (start, end) = QuestionsSection(lines);
        var found = new List<(int, List<string>, bool)>();
        var seen = new HashSet<int>();
        for (var i = start; i < end; i++)
        {
            if (Text.Match(lines[i], @"^\s*-\s*(Q\d+):\s*") is not { Success: true } m ||
                !int.TryParse(m.Groups[1].Value.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
                !seen.Add(number))
            {
                continue;
            }

            var block = new List<string> { lines[i] };
            while (i + 1 < end && Text.IsMatch(lines[i + 1], @"^\s{2,}\S"))
            {
                block.Add(lines[++i]);
            }

            found.Add((number, block, IsAnswered(block[0])));
        }

        return found;
    }

    /// <summary>
    /// The first <c>## Questions</c> section of a card's lines: the lines after its heading, up to the
    /// next heading. <c>(-1, -1)</c> when the card has no such section.
    /// </summary>
    private static (int Start, int End) QuestionsSection(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!Text.IsMatch(lines[i], "^## Questions"))
            {
                continue;
            }

            var end = i + 1;
            while (end < lines.Count && !Text.IsMatch(lines[end], "^## "))
            {
                end++;
            }

            return (i + 1, end);
        }

        return (-1, -1);
    }

    /// <summary>The line of question <paramref name="number"/> in a card's questions, or -1.</summary>
    private static int QuestionAt(List<string> lines, int number)
    {
        var (start, end) = QuestionsSection(lines);
        for (var i = start; i < end; i++)
        {
            if (Text.Match(lines[i], @"^\s*-\s*(Q\d+):\s*") is { Success: true } m &&
                int.TryParse(m.Groups[1].Value.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                n == number)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether a question line carries the page's answer after its choices (MOVE-2).</summary>
    private static bool IsAnswered(string line) => Text.IsMatch(line, @"^.*\[[^\]]+\]\s*->\s*\*\*");

    /// <summary>A question line as its own text, without the answer the page appended.</summary>
    private static string WithoutAnswer(string line) =>
        Text.Match(line, @"^(.*?)\s*->\s*\*\*.*$") is { Success: true } m ? m.Groups[1].Value : line;

    /// <summary>A line compared as text: trimmed, with white space runs made one space.</summary>
    private static string Norm(string line) => Regex.Replace(line, @"\s+", " ").Trim();

    private static string Flat(string text) =>
        string.Join("\n", CardText.Split(text).Select(l => l.TrimEnd())).Trim();

    private static string StatusOf(List<string> lines)
    {
        foreach (var line in lines)
        {
            var m = Text.Match(line, @"^Status:\s*(.*?)\s*$");
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        return "";
    }

    private static CardFolder? FolderOf(string name) => name.Trim().ToLowerInvariant() switch
    {
        "queue" => CardFolder.Queue,
        "active" => CardFolder.Active,
        "review" => CardFolder.Review,
        "done" => CardFolder.Done,
        _ => null,
    };

    private static string Name(CardFolder folder) => folder.ToString().ToLowerInvariant();
}
