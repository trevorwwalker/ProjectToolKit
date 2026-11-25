// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// One question line of a card (CARD-12): a faithful port of <c>Parse-Question</c>
/// (schedule.ps1 L503-518). A question needs at least two choices; a choice ending
/// <c>-&gt; done|queue|active</c> carries the folder it moves the card to. An answered line has the
/// answer after the bracket instead of ending with it, so it stops matching.
/// </summary>
public static class CardQuestions
{
    /// <summary>
    /// The question <paramref name="line"/> asks, or null when it is not an unanswered question with
    /// two or more choices.
    /// </summary>
    public static CardQuestion? Parse(string line)
    {
        if (Text.Match(line, @"^\s*-\s*(Q\d+):\s*(.+?)\s*\[([^\]]+)\]\s*$") is not { Success: true } m)
        {
            return null;
        }

        var choices = new List<CardChoice>();
        foreach (var raw in m.Groups[3].Value.Split('|'))
        {
            var choice = raw.Trim();
            if (choice.Length == 0)
            {
                continue;
            }

            if (Text.Match(choice, @"^(.*?)\s*->\s*(done|queue|active)$") is { Success: true } d)
            {
                choices.Add(new CardChoice
                {
                    Label = d.Groups[1].Value.Trim(),
                    Destination = d.Groups[2].Value.ToLowerInvariant() switch
                    {
                        "done" => CardFolder.Done,
                        "queue" => CardFolder.Queue,
                        _ => CardFolder.Active,
                    },
                });
            }
            else
            {
                choices.Add(new CardChoice { Label = choice, Destination = null });
            }
        }

        if (choices.Count < 2)
        {
            return null;
        }

        return new CardQuestion
        {
            Number = int.Parse(m.Groups[1].Value.AsSpan(1), CultureInfo.InvariantCulture),
            Text = m.Groups[2].Value,
            Choices = choices,
        };
    }

    /// <summary>
    /// The question the block of lines starting at <paramref name="start"/> asks, or null when that
    /// block is not an unanswered question with two or more choices (CARD-12, 820-A).
    /// <para>
    /// Workers wrap a question over several lines, so a question is the <c>- Qn:</c> line plus its
    /// continuation lines - see <see cref="IsContinuation"/> - joined into one line with a space
    /// where each break was, and <see cref="Parse"/> reads that. <paramref name="end"/> is the last
    /// line of the block, whether the block was a question or not, so a caller reading the section
    /// line by line can go on after it.
    /// </para>
    /// </summary>
    public static CardQuestion? ParseBlock(IReadOnlyList<string> lines, int start, out int end)
    {
        ArgumentNullException.ThrowIfNull(lines);
        end = BlockEnd(lines, start);
        return start >= 0 && start < lines.Count ? Parse(Join(lines, start, end)) : null;
    }

    /// <summary>
    /// The question the block starting at <paramref name="start"/> asked and the label the developer
    /// answered it with, or null when the block is not an answered question (CARD-12, MOVE-2,
    /// 820-A). An answered question is the block's text with <c>-&gt; **label** (developer, ...)</c>
    /// after the closing bracket, so the answer sits on the block's last line - the one the bracket
    /// is on - and the note the page writes under it is no part of it.
    /// </summary>
    public static CardQuestion? ParseAnswered(IReadOnlyList<string> lines, int start, out int end, out string answer)
    {
        ArgumentNullException.ThrowIfNull(lines);
        answer = "";
        end = BlockEnd(lines, start);
        if (start < 0 || start >= lines.Count)
        {
            return null;
        }

        if (Text.Match(Join(lines, start, end), @"^(\s*-\s*Q\d+:\s*.+?\s*\[[^\]]+\])\s*->\s*\*\*(.+?)\*\*") is not { Success: true } m)
        {
            return null;
        }

        if (Parse(m.Groups[1].Value) is not { } asked)
        {
            return null;
        }

        answer = m.Groups[2].Value.Trim();
        return asked;
    }

    /// <summary>The last line of the block that starts at <paramref name="start"/>.</summary>
    private static int BlockEnd(IReadOnlyList<string> lines, int start)
    {
        var end = start;
        while (end + 1 < lines.Count && IsContinuation(lines[end + 1]))
        {
            end++;
        }

        return end;
    }

    // 820-A: the lines that belong to the question above them. A blank line, another question, a
    // section heading and the note the page puts under an answer all end it; 760-A's Q1-Q3 were lost
    // because the reader stopped at the first line and never saw the choices below it.
    private static bool IsContinuation(string line) =>
        line.Trim().Length > 0 &&
        !Text.IsMatch(line, @"^## ") &&
        !Text.IsMatch(line, @"^\s*-\s*Q") &&
        !Text.IsMatch(line, @"^\s*Note:");

    // The block as one line: each line trimmed, the breaks between them one space.
    private static string Join(IReadOnlyList<string> lines, int start, int end)
    {
        var text = new StringBuilder(lines[start].Trim());
        for (var i = start + 1; i <= end; i++)
        {
            text.Append(' ').Append(lines[i].Trim());
        }

        return text.ToString();
    }
}
