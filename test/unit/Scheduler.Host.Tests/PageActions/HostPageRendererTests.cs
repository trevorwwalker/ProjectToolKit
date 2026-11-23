using Scheduler.Core.Contracts;
using Scheduler.Web.Views;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 938-B (request #003): the host hands the page the two figures the Worker types panel was guessing -
/// the pool's real total (WT-C1) and scheduler.json's agents in config order (CFG-10, WT-P3) - read on
/// every render, so what the panel shows is what the scheduler is working to and what Save writes back.
/// The rendering itself is the Web category's (<c>PageWorkerTypesTests</c>); these tests check the hand-off,
/// through the same renderer <see cref="HostComposition"/> gives the served page.
/// </summary>
public sealed class HostPageRendererTests
{
    // A pool of 3 with two types: no type uses the "opus" agent, which the panel must still offer (WT-P3).
    private const string PoolThree = """
        {"pool":{"maxWorkers":3,"types":{
          "Local":{"areas":"A","model":"gpt-5-mini"},
          "Sonnet":{"areas":"B","agent":"claude","model":"claude-sonnet-5"}}}}
        """;

    private static IReadOnlyDictionary<string, AgentDefinition> Agents(params string[] names)
    {
        var agents = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [AgentDefinition.DefaultName] = Scheduler.Core.Config.Agents.DefaultAgent(new SchedulerConfig()),
        };
        foreach (var name in names)
        {
            agents[name] = new AgentDefinition { Name = name, Cli = "claude" };
        }

        return agents;
    }

    // The Worker types panel's total field, as the served page carries it.
    private static string TotalField(string page)
    {
        var at = page.IndexOf("<input name='maxWorkers'", StringComparison.Ordinal);
        Assert.True(at >= 0, "the page has no Total workers field");
        return page[at..(page.IndexOf('>', at) + 1)];
    }

    // The agent choice of one row, identified by the type whose name is selected in it.
    private static string AgentSelect(string page, string typeNamed)
    {
        var row = page.IndexOf($"<input name='wname' value='{typeNamed}'", StringComparison.Ordinal);
        Assert.True(row >= 0, $"the page has no row for type {typeNamed}");
        var from = page.IndexOf("<select name='wagent'", row, StringComparison.Ordinal);
        Assert.True(from >= 0, $"the row for {typeNamed} has no agent choice");
        var to = page.IndexOf("</select>", from, StringComparison.Ordinal);
        return page[from..(to + "</select>".Length)];
    }

    // WT-C1, WT-W5: the panel's total is the pool's, not the number of folders the snapshot lists -
    // once a worker is adopted above the total, the folders are more than the total and Save used to
    // write that bigger number back into workers.json.
    [Fact]
    public async Task Page_WorkerTypesPanel_TotalIsThePools_NotTheFoldersListed()
    {
        using var harness = new PageActionHarness(workersJson: PoolThree, project: "ptk-938b-total", hostRenderer: true);
        harness.Tick_Once();
        harness.Running(6, "010-A-x", "A"); // WT-W5: a folder above maxWorkers keeps its row while it runs

        var page = await harness.Client.GetStringAsync("/");

        Assert.Equal(3, harness.Tick.Workers.Slots);
        Assert.Contains("value='3'", TotalField(page), StringComparison.Ordinal);
        Assert.Contains("1 of 3 workers", page, StringComparison.Ordinal); // the Control window's header, same total
    }

    // WT-C1: the total is read on every render, so a page rebuilt after workers.json changed shows the
    // new figure rather than the one captured when the host was put together.
    [Fact]
    public async Task Page_WorkerTypesPanel_TotalReadPerRender()
    {
        using var harness = new PageActionHarness(workersJson: PoolThree, project: "ptk-938b-render", hostRenderer: true);
        harness.Tick_Once();

        Assert.Contains("value='3'", TotalField(await harness.Client.GetStringAsync("/")), StringComparison.Ordinal);

        harness.Tick.Workers.Apply(
            """{"pool":{"maxWorkers":5,"types":{"Local":{"areas":"A"},"Sonnet":{"areas":"B","agent":"claude"}}}}""",
            []);

        Assert.Equal(5, harness.Tick.Workers.Slots);
        Assert.Contains("value='5'", TotalField(await harness.Client.GetStringAsync("/")), StringComparison.Ordinal);
    }

    // CFG-10, WT-P3: the panel offers every agent scheduler.json has, in config order, including one no
    // saved type uses - which is the agent the developer could not choose.
    [Fact]
    public async Task Page_WorkerTypesPanel_OfferEveryConfigAgentInConfigOrder()
    {
        using var harness = new PageActionHarness(
            workersJson: PoolThree,
            project: "ptk-938b-agents",
            agents: Agents("claude", "opus"),
            hostRenderer: true);
        harness.Tick_Once();

        var page = await harness.Client.GetStringAsync("/");

        var choices = AgentSelect(page, "Local");
        Assert.Contains("<option value='opus'>opus</option>", choices, StringComparison.Ordinal);
        var at = new[] { "<option value='' selected>", "<option value='default'>", "<option value='claude'>", "<option value='opus'>" }
            .Select(choice => choices.IndexOf(choice, StringComparison.Ordinal))
            .ToList();
        Assert.All(at, i => Assert.True(i >= 0, string.Join(",", at)));
        Assert.True(at.SequenceEqual(at.Order()), $"agents are not in config order: {choices}");

        // A type that does name an agent has it selected, and still sees the agent it does not use.
        Assert.Contains("<option value='claude' selected>", AgentSelect(page, "Sonnet"), StringComparison.Ordinal);
    }
}
