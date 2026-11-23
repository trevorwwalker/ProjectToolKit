using Scheduler.Core.Moves;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>1056-B: Send back puts the developer's note in the card's <c>## Dev</c> section.</summary>
public class DevNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 20, 30, 0, TimeSpan.Zero);

    private static CardEdit Edit(string text) => new() { Text = text, Path = "a", NewPath = "a" };

    [Fact]
    public void DevNote_NoSection_CreatesItBeforeResult()
    {
        var text = DevNote.Add(Edit("# T\n\nStatus: ready\n\n## Result\n- x\n"), "went back to 8", "sent back from review.", Now).Text;
        Assert.Equal("# T\n\nStatus: ready\n\n## Dev\n\n- 2026-11-21 20:30 developer (from the page): went back to 8\n\n## Result\n- x\n", text);
    }

    [Fact]
    public void DevNote_ExistingSection_AppendsAfterItsLastLine()
    {
        var text = DevNote.Add(Edit("## Dev\n\n- old\n\n## Result\n"), "new", "sent back.", Now).Text;
        Assert.Equal("## Dev\n\n- old\n- 2026-11-21 20:30 developer (from the page): new\n\n## Result\n", text);
    }

    [Fact]
    public void DevNote_EmptyNote_SaysOnlySentBack_NoEmptyQuote()
    {
        var text = DevNote.Add(Edit("## Result\n"), "  \n ", "sent back from review.", Now).Text;
        Assert.Contains("(from the page): sent back from review.", text);
        Assert.DoesNotContain("\"\"", text);
    }

    [Fact]
    public void DevNote_LeadingHash_AndNewLines_StayOneListItem()
    {
        var text = DevNote.Add(Edit("## Result\n"), "# big\n# more", "x", Now).Text;
        Assert.Contains("- 2026-11-21 20:30 developer (from the page): # big # more\n", text);
    }

    [Fact]
    public void DevNote_CrLf_KeptAndFailedEditUntouched()
    {
        Assert.DoesNotContain("\n\n", DevNote.Add(Edit("## Result\r\n"), "n", "x", Now).Text.Replace("\r\n", "|"));
        var failed = CardEdit.Failed("no");
        Assert.Same(failed, DevNote.Add(failed, "n", "x", Now));
    }

    [Fact]
    public void DevNote_SurvivesAWorkerFinish()
    {
        var main = DevNote.Add(Edit("## Questions\n\n## Result\n"), "keep me", "x", Now).Text;
        var worker = "## Questions\n\n## Result\n- done\n";
        Assert.Contains("keep me", CardRequests.KeepDev(main, worker));
    }
}
