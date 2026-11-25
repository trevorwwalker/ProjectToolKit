// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Config;

/// <summary>
/// The agents of scheduler.json: the default one built from the <c>worker</c> section (CFG-9),
/// the named ones of <c>cfg.agents</c> (CFG-10), choosing one by name (CFG-11) and the
/// placeholder expansion of CFG-12. Test plan: scheduler-next/test-plans/config.md.
/// </summary>
public class AgentsTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    // CFG-9 (L210, L224-225): the default agent's format is guessed from worker.args, and it
    // never has an endpoint of its own; the rest of it is the worker section.
    [Fact]
    public void Config_DefaultAgentFormat_GuessedFromArgs()
    {
        Assert.Equal(
            AgentFormat.Copilot,
            Load("""{"worker":{"args":"-p {prompt} --output-format json"}}""").Agents[AgentDefinition.DefaultName].Format);
        Assert.Equal(
            AgentFormat.Copilot,
            Load("""{"worker":{"args":"-p {prompt} --output-format=json"}}""").Agents[AgentDefinition.DefaultName].Format);
        Assert.Equal(
            AgentFormat.Text,
            Load("""{"worker":{"args":"-p {prompt} --quiet"}}""").Agents[AgentDefinition.DefaultName].Format);
        Assert.Equal(
            AgentFormat.Copilot,
            Load("""{"worker":{"args":["-p","{prompt}","--output-format","json"]}}""").Agents[AgentDefinition.DefaultName].Format);

        var cfg = Load("""{"worker":{"cli":"copilot","args":"-p {prompt}","endpoint":"http://c"}}""");
        var def = cfg.Agents[AgentDefinition.DefaultName];
        Assert.Equal(AgentDefinition.DefaultName, def.Name);
        Assert.Equal(cfg.WorkerCli, def.Cli);
        Assert.Equal(cfg.WorkerArgs, def.Args);
        Assert.Equal(cfg.QuotaPatterns, def.QuotaPatterns);
        Assert.False(def.HasEndpoint);
        Assert.Equal("", def.Endpoint);
    }

    // CFG-10 (L226-238): a named agent takes its name as the cli, guesses its format from its own
    // args, and gets the three default quota patterns when it names none.
    [Fact]
    public void Config_NamedAgent_DefaultsAndFormatGuess()
    {
        var cfg = Load("""
            { "agents": {
                "claude": { "args": "-p {prompt} --output-format stream-json" },
                "bare": {}
            } }
            """);

        var claude = cfg.Agents["claude"];
        Assert.Equal("claude", claude.Name);
        Assert.Equal("claude", claude.Cli);
        Assert.Equal(AgentFormat.Claude, claude.Format);
        Assert.Equal(
            new[] { "usage limit", "rate limit", "Failed to authenticate" },
            claude.QuotaPatterns);
        Assert.Equal("-p {prompt} --output-format stream-json", claude.Args);

        var bare = cfg.Agents["bare"];
        Assert.Equal("bare", bare.Cli);
        Assert.Equal(AgentFormat.Text, bare.Format);

        // A named agent's args are its own: nothing is inherited from the worker section.
        Assert.Equal("", bare.Args);
        Assert.False(bare.HasEndpoint);

        // stream-json is guessed first, so a claude that also asks for json is still claude.
        var both = Load("""{"agents":{"c":{"args":"--output-format stream-json --verbose"}}}""").Agents["c"];
        Assert.Equal(AgentFormat.Claude, both.Format);

        // An explicit format wins over the guess (L235).
        Assert.Equal(
            AgentFormat.Text,
            Load("""{"agents":{"x":{"args":"--output-format json","format":"text"}}}""").Agents["x"].Format);
        Assert.Equal(
            AgentFormat.Claude,
            Load("""{"agents":{"x":{"args":"","format":"claude"}}}""").Agents["x"].Format);

        // cli, quota patterns and model given in the file are kept as they are.
        var set = Load("""{"agents":{"x":{"cli":"claude-code","quotaPatterns":["out of quota"],"model":"m","env":{"K":"V"}}}}""").Agents["x"];
        Assert.Equal("claude-code", set.Cli);
        Assert.Equal(new[] { "out of quota" }, set.QuotaPatterns);
        Assert.Equal("m", set.Model);
        Assert.Equal("V", set.Env["K"]);
    }

    // CFG-10 with WRK-4 (L232-234): the endpoint key's presence is recorded separately from its
    // value, so an agent on "" is "has one, empty" and not "has none".
    [Fact]
    public void Config_AgentEndpoint_PresenceRecordedSeparatelyFromValue()
    {
        var cfg = Load("""
            { "agents": {
                "a": { "endpoint": "" },
                "b": {},
                "c": { "endpoint": "http://x/v1" }
            } }
            """);

        Assert.True(cfg.Agents["a"].HasEndpoint);
        Assert.Equal("", cfg.Agents["a"].Endpoint);

        Assert.False(cfg.Agents["b"].HasEndpoint);
        Assert.Equal("", cfg.Agents["b"].Endpoint);

        Assert.True(cfg.Agents["c"].HasEndpoint);
        Assert.Equal("http://x/v1", cfg.Agents["c"].Endpoint);
    }

    // CFG-11 (L239): the named agent when it exists, the default one otherwise - including for
    // no name at all. Keys compare as PowerShell hashtable keys do (G-1).
    [Fact]
    public void Config_AgentOf_UnknownNameGivesDefault()
    {
        var withAgent = Load("""
            { "worker": { "cli": "copilot" },
              "agents": { "claude": { "args": "--output-format stream-json" } } }
            """);

        Assert.Equal("claude", Agents.AgentOf(withAgent, "claude").Name);
        Assert.Equal(AgentFormat.Claude, Agents.AgentOf(withAgent, "claude").Format);
        Assert.Equal("claude", Agents.AgentOf(withAgent, "CLAUDE").Name);

        Assert.Equal(AgentDefinition.DefaultName, Agents.AgentOf(withAgent, "nope").Name);
        Assert.Equal(AgentDefinition.DefaultName, Agents.AgentOf(withAgent, "").Name);
        Assert.Equal(AgentDefinition.DefaultName, Agents.AgentOf(withAgent, null).Name);

        // With no agents at all every name gives the default, which is the worker section.
        var noAgents = Load("""{"worker":{"cli":"copilot","args":"-p {prompt}"}}""");
        var def = Agents.AgentOf(noAgents, "claude");
        Assert.Equal(AgentDefinition.DefaultName, def.Name);
        Assert.Equal("copilot", def.Cli);
        Assert.Equal("-p {prompt}", def.Args);
    }

    // CFG-12 (L401): {model}, {endpoint}, {jobs} and {project} are substituted in the args, the
    // env and the health command; a worker with no model or endpoint gets the config's.
    [Fact]
    public void Config_Expand_SubstitutesPlaceholders()
    {
        const string text = "-m {model} -e {endpoint} -j {jobs} -p {project}";
        const string expected = "-m w -e http://c -j 8 -p Proj";

        var json = "{\"worker\":{\"args\":\"" + text + "\",\"env\":{\"K\":\"" + text
            + "\"},\"endpoint\":\"http://c\"},\"health\":{\"command\":\"" + text + "\"}}";
        var cfg = Load(json);

        Assert.Equal(expected, Agents.Expand(cfg, cfg.WorkerArgs, 8, "w"));
        Assert.Equal(expected, Agents.Expand(cfg, cfg.HealthCommand, 8, "w"));
        Assert.Equal(expected, Agents.ExpandEnv(cfg, cfg.WorkerEnv, 8, "w")["K"]);

        // No model of its own: the config's worker.model.
        var withModel = Load("""{"worker":{"model":"cm"}}""");
        Assert.Equal("-m cm", Agents.Expand(withModel, "-m {model}", 4));

        // A worker's own endpoint wins over the config's.
        Assert.Equal("-e http://w", Agents.Expand(cfg, "-e {endpoint}", 8, "w", "http://w"));

        // Only these four placeholders, and nothing else is touched ({prompt} is 160-A's).
        Assert.Equal("-p {prompt} --x {prompt}", Agents.Expand(cfg, "-p {prompt} --x {prompt}", 8, "w"));
        Assert.Equal("", Agents.Expand(cfg, "", 8, "w"));
    }
}
