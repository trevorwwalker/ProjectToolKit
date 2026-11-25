// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// Which paths under the jobs folder are cards (CARD-4) and how a card's text maps onto the
/// <see cref="Card"/> model (CARD-6..CARD-12, CARD-14): a pure port of the parsing half of
/// <c>Read-Cards</c> (schedule.ps1 L591-645). Reading git and the blob cache are Host (040-B);
/// why a card waits (CARD-13) is <see cref="CardWhy"/>, which asks this for the journal lines.
/// PowerShell-side matching is case-insensitive through the <see cref="Text"/> helpers (G-1).
/// </summary>
public static class CardFiles
{
    /// <summary>
    /// CARD-4 (L602): <c>{jobsDir}/(queue|active|review|done)/{num}-{category}-{slug}.md</c>;
    /// any other path is not a card and gives null. Sets Folder, Path, Name, Num, Category and Id
    /// only; a done/ card is the whole card, unopened (CARD-5).
    /// </summary>
    public static Card? FromPath(string jobsDir, string path)
    {
        var m = Text.Match(
            path, @"^" + Regex.Escape(jobsDir) + @"/(queue|active|review|done)/((\d+)-([A-Z])-[^/]+)\.md$");
        if (!m.Success)
        {
            return null;
        }

        var folder = m.Groups[1].Value.ToLowerInvariant() switch
        {
            "queue" => CardFolder.Queue,
            "active" => CardFolder.Active,
            "review" => CardFolder.Review,
            _ => CardFolder.Done,
        };
        // 931-A (developer, 2026-11-19): the number is the card's identity and may be any width.
        // The id keeps the digits exactly as the file name writes them - 007-A stays 007-A and
        // 1001-A is 1001-A - while Num holds the value, so order is numeric (999-A before 1000-A).
        // A number too big to hold is not a card number, so the file is ignored as CARD-4 says.
        if (!long.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
        {
            return null;
        }

        var category = m.Groups[4].Value;
        return new Card
        {
            Folder = folder,
            Path = path,
            Name = m.Groups[2].Value,
            Num = num,
            Category = category,
            Id = m.Groups[3].Value + "-" + category,
        };
    }

    /// <summary>
    /// CARD-4..CARD-11: the card at <paramref name="path"/> as <paramref name="text"/> says.
    /// A done/ card is not opened (CARD-5): its text is ignored. Null when the path is not a card.
    /// </summary>
    public static Card? Parse(string jobsDir, string path, string text, IReadOnlyList<string>? kinds = null)
    {
        var card = FromPath(jobsDir, path);
        return card is null || card.Folder == CardFolder.Done ? card : ParseText(card, text, kinds);
    }

    /// <summary>
    /// Read one card: match the path (CARD-4) and, unless it is in done/, take its text through
    /// <paramref name="readText"/> - which CARD-5 keeps uncalled for a done/ card.
    /// </summary>
    public static Card? Read(string jobsDir, string path, Func<string, string> readText, IReadOnlyList<string>? kinds = null)
    {
        var card = FromPath(jobsDir, path);
        if (card is null || card.Folder == CardFolder.Done)
        {
            return card;
        }

        return ParseText(card, readText(path), kinds);
    }

    /// <summary>
    /// The text fields (CARD-6..CARD-12, CARD-14) onto a copy of <paramref name="card"/>. Field
    /// lines are parsed from the header block - the text before the first <c>## </c> section
    /// (760-A) - and the last of each field there wins; questions and journal lines come from
    /// their own sections (CARD-12, CARD-14).
    /// </summary>
    public static Card ParseText(Card card, string text) => ParseText(card, text, null);

    /// <summary>As above, reading <c>Kind:</c> against <paramref name="kinds"/> (EST-2); null or empty is the default list.</summary>
    public static Card ParseText(Card card, string text, IReadOnlyList<string>? kinds)
    {
        kinds = kinds is { Count: > 0 } ? kinds : SchedulerConfig.DefaultCardKinds;
        var kind = "";
        var kindWord = "";
        var kindSeen = false;
        var title = "";
        var status = "";
        var prio = 1;
        int? timeBox = null;
        string? workerReq = null;
        string? scopeLine = null;
        string? locksLine = null;
        string? headerCategory = null;
        var cardType = "";
        var version = "";
        var planPath = "";
        var planStep = "";
        var planSeen = false;
        IReadOnlyList<string> depends = [];
        var questions = new List<CardQuestion>();
        var result = new List<string>();
        var inStatus = false;
        var inQuestions = false;
        var inResult = false;
        var inHeader = true;

        var cardLines = text.Split('\n');
        for (var index = 0; index < cardLines.Length; index++)
        {
            var line = cardLines[index];

            // CARD-12, CARD-14 (L612-615): the section decides what a line is for - a question
            // under ## Questions, journal text under ## Result - and the section ends at the next
            // `## ` header. Field lines below are parsed in every section (L611-626).
            if (Text.IsMatch(line, @"^## "))
            {
                inQuestions = Text.IsMatch(line, @"^## Questions");
                inResult = Text.IsMatch(line, @"^## Result");
                inHeader = false;   // 760-A: the fields below are header fields only
            }
            else if (inResult)
            {
                result.Add(line);
            }
            else if (inQuestions && CardQuestions.ParseBlock(cardLines, index, out var questionEnd) is { } question)
            {
                // CARD-12 (820-A): a question the worker wrapped is one question; the lines under it
                // belong to it, so the read goes on after the block, not at its second line.
                questions.Add(question);
                index = questionEnd;
            }

            // CARD-6 (L617): the first line that starts like "# "; "## x" is not one.
            if (title == "" && Text.Match(line, @"^#\s+(.*)$") is { Success: true } t)
            {
                title = t.Groups[1].Value.Trim();
            }

            // CARD-7 (L618-620): while in the first Status field, lines indented by 2+ spaces
            // are appended with a space and parse as nothing else.
            if (inStatus && Text.IsMatch(line, @"^\s{2,}\S"))
            {
                status += " " + line.Trim();
                continue;
            }

            inStatus = false;

            // CARD-6..CARD-11 (760-A): the fields are read from the header block - the text before
            // the first "## " section - not from every line. A Task sentence wrapped so that a line
            // begins "worker: <paths>" used to replace the card's Worker: line, no worker fitted it,
            // and the card sat in queue/ forever (530-D).
            if (!inHeader)
            {
                continue;
            }

            if (status == "" && Text.Match(line, @"^Status:\s*(.*)$") is { Success: true } s)
            {
                status = s.Groups[1].Value.Trim();
                inStatus = true;
            }

            // CARD-8 (L621-623): high 0, low 2, any other word 1; a line with no word is no line.
            if (Text.Match(line, @"^Priority:\s*(\w+)") is { Success: true } p)
            {
                prio = Text.Same(p.Groups[1].Value, "high") ? 0
                     : Text.Same(p.Groups[1].Value, "low") ? 2
                     : 1;
            }

            // CARD-9 (L624): minutes; 0 means unset, as the script's falsy 0 does.
            if (Text.Match(line, @"^Time box:\s*(\d+)") is { Success: true } b)
            {
                var minutes = int.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture);
                timeBox = minutes > 0 ? minutes : null;
            }

            // CARD-10 (L625): the text before any "(", trimmed.
            if (Text.Match(line, @"^Worker:\s*(\S[^(]*?)\s*(\(.*)?$") is { Success: true } w)
            {
                workerReq = w.Groups[1].Value;
            }

            // 969-A: the header's category; `Category:` is the old name of the same field and reads the same.
            if (headerCategory is null && Text.Match(line, @"^(?:Category|Lane):\s*(\S+)") is { Success: true } cat)
            {
                headerCategory = cat.Groups[1].Value.Trim();
            }

            // 1030-A: Type, Version and Plan are header fields; the first of each wins. Type is
            // feature or bug in any case, anything else is untyped.
            if (cardType == "" && Text.Match(line, @"^Type:\s*(\w+)") is { Success: true } ty
                && (Text.Same(ty.Groups[1].Value, "feature") || Text.Same(ty.Groups[1].Value, "bug")))
            {
                cardType = ty.Groups[1].Value.ToLowerInvariant();
            }

            // EST-1..EST-3: Kind is a header field read like Type: the first line wins, its first word is matched
            // against the project's list ignoring case and kept lower case; unknown, empty or missing is "".
            if (!kindSeen && Text.Match(line, @"^Kind:[ \t]*(\S*)") is { Success: true } ki)
            {
                kindSeen = true;
                kindWord = ki.Groups[1].Value;
                var known = kinds.FirstOrDefault(k => Text.Same(k, kindWord));
                kind = known is null || kindWord.Length == 0 ? "" : known.ToLowerInvariant();
            }

            if (version == "" && Text.Match(line, @"^Version:\s*(\S.*?)\s*$") is { Success: true } ve)
            {
                version = ve.Groups[1].Value;
            }

            if (!planSeen && Text.Match(line, @"^Plan:\s*(.*?)\s*$") is { Success: true } pl)
            {
                planSeen = true;
                (planPath, planStep) = SplitPlan(pl.Groups[1].Value);
            }

            // SC-1 (job card 560-A): the first Scope and Locks lines, for the card's scope.
            if (scopeLine is null && Text.Match(line, @"^Scope:\s*(.*)$") is { Success: true } sc)
            {
                scopeLine = sc.Groups[1].Value.Trim();
            }

            if (locksLine is null && Text.Match(line, @"^Locks:\s*(.*)$") is { Success: true } lk)
            {
                locksLine = lk.Groups[1].Value.Trim();
            }

            // CARD-11 (L626-628, 931-A): every \b\d+-[A-Z]\b on the line - a card number is any
            // width; the script's [regex] call is case-sensitive .NET code (G-1), so a lowercase id
            // is no id.
            if (Text.Match(line, @"^Depends on:\s*(.*)$") is { Success: true } d)
            {
                depends = [.. Regex.Matches(d.Groups[1].Value, @"\b\d+-[A-Z]\b").Select(x => x.Value)];
            }
        }

        var (why, whyFrom) = CardWhy.For(card.Folder, status, result);

        return card with
        {
            Title = title,
            Status = status,
            Prio = prio,
            TimeBox = timeBox,
            WorkerReq = workerReq,
            ScopeLine = scopeLine,
            LocksLine = locksLine,
            HeaderCategory = headerCategory,
            CardType = cardType,
            Kind = kind,
            UnknownKind = kind == "" ? kindWord : "",
            KindList = kinds,
            Version = version,
            PlanPath = planPath,
            PlanStep = planStep,
            Depends = depends,
            Questions = questions,
            LatestJournal = CardJournal.Latest(result),
            Why = why,
            WhyFrom = whyFrom,
        };
    }

    /// <summary>
    /// 1030-A: a <c>Plan:</c> value is the plan file, then its step or phase as free text. An empty
    /// value and "none" (with anything after it) mean no plan.
    /// </summary>
    public static (string Path, string Step) SplitPlan(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || Text.IsMatch(value, @"^none\b"))
        {
            return ("", "");
        }

        var m = Text.Match(value, @"^(\S+?)[,;:]?(?:\s+(.*))?$");
        return (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value.Trim() : "");
    }
}

/// <summary>
/// EST-3: says that a card's <c>Kind:</c> word is not on the project's list once per card and blob, not on every
/// tick that reads the card again.
/// </summary>
public sealed class UnknownKindLog
{
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);

    /// <summary>The line to log for <paramref name="card"/> read from blob <paramref name="blobId"/>, or null when it has none or was said already.</summary>
    public string? Once(Card card, string blobId)
    {
        if (card.UnknownKind.Length == 0 || !_said.Add(card.Name + "|" + blobId))
        {
            return null;
        }

        return $"card {card.Name}: Kind: {card.UnknownKind} is not one of {string.Join(", ", card.KindList)}";
    }
}
