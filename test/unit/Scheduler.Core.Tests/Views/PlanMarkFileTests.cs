using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>1125-C: the developer's marks on plans and steps: the cycle, the file, and what a mark does to its parent.</summary>
public class PlanMarkFileTests
{
    private const string Plan = "jobs/plans/x_plan.md";
    private const string Doc = "# T\n\n## A\n\n### a1\n\n### a2\n\n## B\n";

    private static ViewCard Card(string id, ViewFolder folder, string step) =>
        new() { Id = id, Number = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture), Category = "C", Folder = folder, PlanPath = Plan, PlanStep = step };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> Marks(params (string Step, ManualMark Mark)[] marks) =>
        new Dictionary<string, IReadOnlyDictionary<string, ManualMark>> { [Plan] = marks.ToDictionary(m => m.Step, m => m.Mark) };

    private static PlanNode Tree(IEnumerable<ViewCard> cards, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks) =>
        PlanTree.Of(cards, new HashSet<string> { Plan }, new Dictionary<string, string> { [Plan] = Doc }, null, marks)[0];

    [Fact]
    public void Cycle_IsAutomaticCheckedClearedAutomatic()
    {
        var mark = ManualMark.Automatic;
        var seen = new List<ManualMark>();
        for (var i = 0; i < 4; i++)
        {
            mark = PlanMarkFile.Next(mark);
            seen.Add(mark);
        }

        Assert.Equal([ManualMark.Checked, ManualMark.Cleared, ManualMark.Automatic, ManualMark.Checked], seen);
    }

    [Fact]
    public void File_RoundTrips_AndAutomaticRemovesTheEntry()
    {
        var text = PlanMarkFile.With(null, Plan, "3", ManualMark.Checked);
        text = PlanMarkFile.With(text, Plan, "3.1", ManualMark.Cleared);

        var read = PlanMarkFile.Read(text);
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(read, Plan, "3"));
        Assert.Equal(ManualMark.Cleared, PlanMarkFile.Of(read, Plan, "3.1"));
        Assert.Equal(text, PlanMarkFile.Write(read));
        Assert.Contains("\"3.1\": \"cleared\"", text, StringComparison.Ordinal);

        var back = PlanMarkFile.With(PlanMarkFile.With(text, Plan, "3", ManualMark.Automatic), Plan, "3.1", ManualMark.Automatic);
        Assert.Empty(PlanMarkFile.Read(back));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void File_AnythingUnreadable_HasNoMarks(string? text) => Assert.Empty(PlanMarkFile.Read(text));

    [Fact]
    public void CheckedStep_MakesTheParentComplete_WhenTheOthersAre()
    {
        var node = Tree([Card("1-C", ViewFolder.Done, "step 2"), Card("2-C", ViewFolder.Queue, "step 1")], Marks(("1", ManualMark.Checked)));

        Assert.Equal(PlanMark.Complete, node.Steps[0].Mark);
        Assert.Equal(ManualMark.Checked, node.Steps[0].Manual);
        Assert.Equal(PlanMark.Complete, node.Mark);
    }

    [Fact]
    public void ClearedStep_KeepsTheParentOpen()
    {
        var node = Tree([Card("1-C", ViewFolder.Done, "step 1"), Card("2-C", ViewFolder.Done, "step 2")], Marks(("2", ManualMark.Cleared)));

        Assert.Equal(PlanMark.Open, node.Steps[1].Mark);
        Assert.Equal(PlanMark.Open, node.Mark);
        Assert.False(node.Complete);
    }

    [Fact]
    public void SubStepMark_FeedsItsStep_AndThePlanOwnMarkWinsOverAll()
    {
        var node = Tree([Card("1-C", ViewFolder.Done, "step 1.1"), Card("2-C", ViewFolder.Done, "step 1.2"), Card("3-C", ViewFolder.Done, "step 2")], Marks(("1.2", ManualMark.Cleared)));
        Assert.Equal(PlanMark.Open, node.Steps[0].Mark);

        var forced = Tree([Card("3-C", ViewFolder.Queue, "step 2")], Marks((PlanMarkFile.PlanKey, ManualMark.Checked)));
        Assert.Equal(PlanMark.Complete, forced.Mark);
    }

    [Fact]
    public void AMarkForAStepThatDoesNotExist_IsIgnored()
    {
        var plain = Tree([Card("1-C", ViewFolder.Done, "step 1")], null);
        var with = Tree([Card("1-C", ViewFolder.Done, "step 1")], Marks(("9", ManualMark.Cleared), ("1.7", ManualMark.Checked)));

        Assert.Equal(plain.Mark, with.Mark);
        Assert.Equal(plain.Steps.Select(s => s.Mark), with.Steps.Select(s => s.Mark));
    }

    [Fact]
    public void Snapshot_VersionMoves_WhenAMarkChanges()
    {
        var a = ViewsSnapshot.Build("p", [], []);
        var b = ViewsSnapshot.Build("p", [], [], marks: Marks(("1", ManualMark.Checked)));

        Assert.NotEqual(a.Version, b.Version);
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(b.Marks, Plan, "1"));
    }
}
