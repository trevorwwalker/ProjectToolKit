using Scheduler.Core.WebContract;
using Scheduler.Host.PageActions;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>1033-C: the Projects window's Workspaces box writes one project's <c>workspaces</c> to the pool file.</summary>
public class WorkspacesEditTests
{
    private const string Pool = """{ "port": 8123, "projects": [ { "name": "A", "repo": "C:\\a", "weight": 70, "workspaces": 2 }, { "name": "B", "repo": "C:\\b", "workspaces": 5 }, { "name": "C", "repo": "C:\\c" } ] }""";

    private static (PoolConfigFile File, PoolPageActions Actions, List<string> Wakes) Rig(TempDir temp)
    {
        var path = Path.Combine(temp.Path, "pool.json");
        File.WriteAllText(path, Pool);
        var log = new ListLog(); var file = new PoolConfigFile(path, log);
        Assert.True(file.Read(), string.Join(" / ", log.Lines));
        var wakes = new List<string>();
        var actions = new PoolPageActions(new PoolPageActionParts
        {
            Projects = () => [("A", null!), ("B", null!), ("C", null!)],
            Wake = () => wakes.Add("wake"),
            ClearAgent = _ => true,
            WritePool = file.WritePool,
            WriteWorkspaces = file.WriteWorkspaces,
        });
        return (file, actions, wakes);
    }

    // The posted value is written to that project only; the others and the rest of the file stay, and the file reads back.
    [Fact]
    public void Card1033_PostedValue_WrittenToThatProjectOnly_RoundTrips()
    {
        using var temp = new TempDir();
        var (file, actions, wakes) = Rig(temp);

        Assert.True(actions.SetWorkspaces("b", "9").Ok);   // names compare without case

        var again = new PoolConfigFile(file.Path, new ListLog());
        Assert.True(again.Read());
        Assert.Equal([(int?)2, 9, null], again.Current!.Projects.Select(p => p.Workspaces));
        Assert.Equal(["A", "B", "C"], again.Current.Projects.Select(p => p.Name));
        Assert.Equal(70, again.Current.Projects[0].Weight);
        Assert.Contains("\"port\": 8123", File.ReadAllText(file.Path), StringComparison.Ordinal);
        var text = File.ReadAllText(file.Path);
        Assert.Contains("""C:\\a""", text, StringComparison.Ordinal);   // backslashes stay escaped
        Assert.Single(wakes);
    }

    // A project with no setting gets one; a value not a whole number 1..64 is refused, naming the project, and nothing is written.
    [Theory]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("x")]
    [InlineData("")]
    public void Card1033_OutOfRange_Refused_FileUntouched(string value)
    {
        using var temp = new TempDir();
        var (file, actions, wakes) = Rig(temp);
        var before = File.ReadAllText(file.Path);

        var result = actions.SetWorkspaces("C", value);

        Assert.False(result.Ok);
        Assert.StartsWith("C:", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(file.Path));
        Assert.Empty(wakes);
    }

    [Fact]
    public void Card1033_NoSettingYet_IsAdded_AndUnknownProjectRefused()
    {
        using var temp = new TempDir();
        var (file, actions, _) = Rig(temp);

        Assert.True(actions.SetWorkspaces("C", "64").Ok);
        Assert.False(actions.SetWorkspaces("Nope", "3").Ok);

        var again = new PoolConfigFile(file.Path, new ListLog());
        Assert.True(again.Read());
        Assert.Equal(64, again.Current!.Projects[2].Workspaces);
    }
}
