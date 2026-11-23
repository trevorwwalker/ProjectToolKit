using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// The environment a worker starts with (WRK-8; <c>Start-Worker</c> L1795-1813): another agent's
/// variables are removed, this agent's are set with the placeholders filled in, and
/// <c>{contextLength}</c> comes from the model server. Test plan: test-plans/workers.md.
/// </summary>
public class WorkerEnvTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";
    private const string Server = "http://s/v1";

    private sealed class FakeLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }

    /// <summary>A model server whose <c>/v1/models</c> text is fixed; null means it says nothing.</summary>
    private sealed class FakeProbe : IServerProbe
    {
        public string? ModelsJson { get; set; }

        public int ModelReads { get; private set; }

        public bool IsUp(string endpoint) => true;

        public string? ReadModels(string endpoint)
        {
            ModelReads++;
            return ModelsJson;
        }

        public string? ReadMetrics(string metricsUrl) => null;
    }

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    private static EffectiveWorker Worker(string agent, string model = "m1", string endpoint = "") =>
        new() { Agent = agent, Model = model, Endpoint = endpoint };

    private static WorkerEnvironment Build(
        SchedulerConfig config,
        string agent,
        EffectiveWorker worker,
        FakeProbe probe,
        FakeLog log,
        IReadOnlyDictionary<string, string>? processEnv = null)
    {
        return WorkerEnv.Build(
            config, Agents.AgentOf(config, agent), worker, jobs: 8, slot: 1, processEnv, probe, log);
    }

    // WRK-8 (L1802): a variable another agent sets but this one does not is removed; this agent's
    // own variables are set with {model} filled in.
    [Fact]
    public void Workers_Environment_OtherAgentsVarsRemovedOwnSet()
    {
        var cfg = Load("""
            {"agents":{
               "A":{"cli":"a","env":{"X":"1"}},
               "B":{"cli":"b","env":{"Y":"{model}"}}}}
            """);
        var process = new Dictionary<string, string>(Text.Comparer) { ["X"] = "old" };

        var env = Build(cfg, "B", Worker("B", "m1"), new FakeProbe(), new FakeLog(), process);

        Assert.Equal(["X"], env.Removed);
        Assert.Equal("m1", env.Set["Y"]);
        Assert.False(env.Set.ContainsKey("X"));
    }

    // WRK-8 (L1805-1812): {contextLength} and {contextLength-N} are the server's max_model_len
    // minus N, never below 1024; with no answer both variables stay unset and it is logged once.
    [Fact]
    public void Workers_ContextLength_FromServerMinusN()
    {
        var cfg = Load("""
            {"agents":{"srv":{"cli":"c","endpoint":"http://s/v1",
              "env":{"C":"{contextLength-2000}","D":"{contextLength}"}}}}
            """);
        var worker = Worker("srv", "m1", Server);

        var probe = new FakeProbe { ModelsJson = """{"data":[{"id":"m1","max_model_len":32768}]}""" };
        var env = Build(cfg, "srv", worker, probe, new FakeLog());
        Assert.Equal("30768", env.Set["C"]);
        Assert.Equal("32768", env.Set["D"]);
        Assert.Equal(1, probe.ModelReads);

        probe = new FakeProbe { ModelsJson = """{"data":[{"id":"other","max_model_len":2500}]}""" };
        env = Build(cfg, "srv", worker, probe, new FakeLog());
        Assert.Equal("1024", env.Set["C"]);
        Assert.Equal("2500", env.Set["D"]);

        var quiet = new FakeLog();
        env = Build(cfg, "srv", worker, new FakeProbe(), quiet);
        Assert.False(env.Set.ContainsKey("C"));
        Assert.False(env.Set.ContainsKey("D"));
        Assert.Single(quiet.Lines);
        Assert.Contains("no context length", quiet.Lines[0]);
    }

    // Dynamic locks (DL-18): the card, the lock exe and core.hooksPath through GIT_CONFIG_*.
    [Fact]
    public void Workers_Environment_LocksOn_SetsCardExeAndHooksPath()
    {
        var cfg = Load("""{"agents":{"A":{"cli":"a","env":{"PTK_CARD":"agent's own"}}}}""");
        var hooks = WorkerFiles.HooksDir(@"C:\log");
        var locks = new WorkerLocks("1019-A-x", @"C:\tk\scheduler-next.exe", hooks);

        var env = WorkerEnv.Build(cfg, Agents.AgentOf(cfg, "A"), Worker("A"), 8, 1, null, new FakeProbe(), new FakeLog(), locks);

        Assert.Equal("1019-A-x", env.Set["PTK_CARD"]);   // the scheduler's wins over the agent's
        Assert.Equal(@"C:\tk\scheduler-next.exe", env.Set["PTK_SCHEDULER_EXE"]);
        Assert.Equal("1", env.Set["GIT_CONFIG_COUNT"]);
        Assert.Equal("core.hooksPath", env.Set["GIT_CONFIG_KEY_0"]);
        Assert.Equal(@"C:\log\hooks", env.Set["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("C:/repos/Proj/.git", env.Set["PTK_REPO"]);
    }

    // With locks off, the environment is as before.
    [Fact]
    public void Workers_Environment_LocksOff_SetsNoLockVariables()
    {
        var cfg = Load("""{"agents":{"A":{"cli":"a"}}}""");

        var env = Build(cfg, "A", Worker("A"), new FakeProbe(), new FakeLog());

        foreach (var key in new[] { "PTK_CARD", "PTK_REPO", "PTK_SCHEDULER_EXE", "GIT_CONFIG_COUNT", "GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0" })
        {
            Assert.False(env.Set.ContainsKey(key), key);
        }
    }
}
