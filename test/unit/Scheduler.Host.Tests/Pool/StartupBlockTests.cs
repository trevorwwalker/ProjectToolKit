// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Host.Pool;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>Card 1012-B: the <c>startup:</c> block the log opens with.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class StartupBlockTests
{
    private const string Secret = "sk-SECRET-VALUE-123";

    // 1012-B: version, system lines, one line per worker type and per project, no env value.
    [Fact]
    public void Block_HoldsVersionSystemTypesProjects_AndNoEnvValue()
    {
        var pool = PoolConfig.Parse("""
            { "port": 9001, "listen": "lan", "remoteControl": true,
              "projects": [ { "name": "A", "repo": "C:/a" }, { "name": "B", "repo": "C:/b" } ] }
            """);
        var types = new WorkerPool
        {
            MaxWorkers = 3,
            Types = [
                new WorkerType { Name = "local", Max = 2, Agent = "x", Model = "m1", Endpoint = "http://srv:1234" },
                new WorkerType { Name = "big", Max = 1, Agent = "x", Model = "m2" }],
            DefaultWorker = "local",
        };
        var agent = new AgentDefinition
        {
            Name = "x",
            Env = new Dictionary<string, string>(Text.Comparer) { ["API_KEY"] = Secret },
        };

        var lines = new List<string>();
        lines.AddRange(StartupBlock.Tool(@"C:\exe\scheduler-next.exe"));
        lines.AddRange(StartupBlock.Machine());
        lines.AddRange(StartupBlock.Pool(@"C:\pool.json", pool, types, @"C:\logs", [agent]));
        foreach (var name in new[] { "A", "B" })
        {
            lines.Add(StartupBlock.Project(
                new SchedulerConfig { Project = name, Repo = "C:/" + name, LogDir = @"C:\logs\" + name, HealthCommand = new string('h', 120) }, 50, planner: true));
        }

        lines.Add(StartupBlock.Options(["-Pool", "p.json", "-Paused"]));

        Assert.All(lines, l => Assert.StartsWith("startup: ", l, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains($"Switchyard {CoreInfo.Version}", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("startup: system: machine ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("logical processors", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("memory", StringComparison.Ordinal) && l.Contains("MB free", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("process id", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("port 9001, listen lan, remote control on, maxWorkers 3", StringComparison.Ordinal));
        Assert.Single(lines, l => l.EndsWith("worker type local: max 2, agent x, model m1", StringComparison.Ordinal));
        Assert.Single(lines, l => l.EndsWith("worker type big: max 1, agent x, model m2", StringComparison.Ordinal));
        Assert.Single(lines, l => l == "startup: pool: default worker type local"); // 1146-A
        Assert.DoesNotContain(lines, l => l.Contains("premium", StringComparison.Ordinal));
        Assert.Single(lines, l => l.EndsWith("model server http://srv:1234", StringComparison.Ordinal));
        Assert.Equal(2, lines.Count(l => l.StartsWith("startup: project ", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.Contains("env keys API_KEY", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("health " + new string('h', 80) + ",", StringComparison.Ordinal));
        Assert.Contains(lines, l => l == "startup: options: -Pool p.json -Paused");
        Assert.DoesNotContain(lines, l => l.Contains(Secret, StringComparison.Ordinal));
    }
}
