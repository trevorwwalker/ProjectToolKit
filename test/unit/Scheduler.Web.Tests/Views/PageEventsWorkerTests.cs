using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 950-C (PAGE-5, WT-P6): the Recent events window's Worker column says who did the work -
/// the worker type's name as workers.json names it. A run from before the accounting held a type
/// shows its model instead, and a run with neither keeps the old slot number.
/// </summary>
public sealed class PageEventsWorkerTests
{
    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

    // The Worker column of the run's row, as the page draws it.
    private static string WorkerCell(PageEvent entry)
    {
        var html = Render(ViewsFixture.Plain() with { Events = [entry] });
        var at = html.IndexOf("<b>Recent events</b>", StringComparison.Ordinal);
        Assert.True(at >= 0, "no Recent events window");
        var row = html.IndexOf("<tr data-card='120-B'>", at, StringComparison.Ordinal);
        Assert.True(row >= 0, "no run row in Recent events");
        // 968-C: the columns are marked, so the Worker column is found by its mark (the second of the
        // columns that never wrap), not by a bare <td>.
        const string Short = "<td class='nw'>";
        var when = html.IndexOf(Short, row, StringComparison.Ordinal);
        var worker = html.IndexOf(Short, when + Short.Length, StringComparison.Ordinal);
        var end = html.IndexOf("</td>", worker, StringComparison.Ordinal);
        return html[(worker + Short.Length)..end];
    }

    // One finished run, as accounting's row would reach the page.
    private static PageEvent Run(string type = "", string model = "", int slot = 2) => new()
    {
        Time = ViewsFixture.Built,
        Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "slot {0}  120-B-y: done, 5 min", slot),
        Slot = slot,
        Card = "120-B-y",
        Result = "done",
        Minutes = 5,
        Type = type,
        Model = model,
    };

    // PAGE-5, WT-P6: a run of type Opus shows "Opus", and nothing of the slot number.
    [Fact]
    public void Events_TypeKnown_WorkerColumnIsTheTypeName()
    {
        Assert.Equal("Opus", WorkerCell(Run(type: "Opus")));
    }

    // WT-P6: the rows written before the type column have no type, so the model says who did it.
    [Fact]
    public void Events_NoType_WorkerColumnIsTheModel()
    {
        Assert.Equal("claude-opus-5-5", WorkerCell(Run(model: "claude-opus-5-5")));
    }

    // WT-P6: with neither type nor model the column stays what it always was, the slot number.
    [Fact]
    public void Events_NoTypeAndNoModel_WorkerColumnIsTheSlotNumber()
    {
        Assert.Equal("2", WorkerCell(Run()));
    }

    // A type name is no longer than the number was: the column carries the name alone.
    [Fact]
    public void Events_TypeKnown_ColumnHoldsNothingButTheName()
    {
        var cell = WorkerCell(Run(type: "Sonnet"));

        Assert.Equal("Sonnet", cell);
        Assert.DoesNotContain("slot", cell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("worker", cell, StringComparison.OrdinalIgnoreCase);
    }
}
