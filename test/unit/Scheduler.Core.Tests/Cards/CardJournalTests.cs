// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// The newest <c>## Result</c> journal entry and the shortening of it (CARD-14, L568-589):
/// the entry with the greatest date/time key wins, the later one in the file on a tie, and an
/// undated card falls back to its loose text. Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class CardJournalTests
{
    // CARD-14. The winning entry is the one dated 2026-11-18 10:30: its text is the whole entry
    // line trimmed (bullet and date included, L581) joined with its continuation lines.
    [Fact]
    public void Cards_LatestJournal_GreatestDateTimeWins()
    {
        string[] lines =
        [
            "- 2026-11-18 09:00 a",
            "* **2026-11-18 10:30** b",
            "  more",
            "2026-11-17 23:00 c",
        ];
        Assert.Equal("* 2026-11-18 10:30 b more", CardJournal.Latest(lines));

        // A time in the first 40 characters joins the key, so 10:30 beats 09:00 on the same day;
        // without the time the first entry would tie with it and the later entry would win.
        Assert.Equal(
            "- 2026-11-18 09:00 a",
            CardJournal.Latest(["- 2026-11-18 09:00 a", "* **2026-11-18** b"]));

        // Tie on the same key: the later entry in the file wins.
        Assert.Equal(
            "- 2026-11-18 09:00 b",
            CardJournal.Latest(["- 2026-11-18 09:00 a", "- 2026-11-18 09:00 b"]));

        // No dated entry: the loose text, joined with single spaces.
        Assert.Equal("no date here and more", CardJournal.Latest(["no date", "here", "", " and more"]));
        Assert.Equal("", CardJournal.Latest([]));
    }

    // CARD-14 (L568-574): ** and ` are removed, runs of spaces and newlines collapse to one
    // space, and text over the limit is cut to it plus "...".
    [Fact]
    public void Cards_ShortText_StripsAndTruncates()
    {
        Assert.Equal("bold and code", CardJournal.ShortText("  **bold** and `code`  "));
        Assert.Equal("a b c", CardJournal.ShortText("a\n\n   b\r\n \t c  "));

        var long400 = new string('x', 400);
        Assert.Equal(new string('x', 300) + "...", CardJournal.ShortText(long400));

        // Boundary: exactly the limit keeps everything and adds nothing.
        Assert.Equal(new string('y', 300), CardJournal.ShortText(new string('y', 300)));

        // The cut is TrimEnd-ed before the ellipsis (L573).
        var cutOnASpace = new string('z', 299) + " " + new string('w', 100);
        Assert.Equal(new string('z', 299) + "...", CardJournal.ShortText(cutOnASpace));

        // The limit is a parameter, as Short-Text takes one.
        Assert.Equal("hello wo...", CardJournal.ShortText("hello world", 8));
    }
}
