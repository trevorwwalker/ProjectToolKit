using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

public class ModelColumnTests
{
    private static readonly ModelEntry[] Models =
    [
        new() { Key = "qwen3", Source = "Qwen/Qwen3-32B" },
        new() { Key = "opus" },
    ];

    // ACCT-2 (Account, schedule.ps1 L848-849): the model that really ran is the models.json key
    // the worker's model resolves to, else the worker's model, else the config model.
    [Fact]
    public void Acct_ModelColumn_KeyElseWorkerElseConfig()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.FromHours(-8)));
        var probe = new FakeServerProbe();
        var cache = new ServerModelCache(clock, probe);

        // The worker's model is a models.json key.
        Assert.Equal("qwen3", ModelColumn.Resolve(Models, "qwen3", "", "config-model", cache));

        // Not a key and no server to ask: the name as configured.
        Assert.Equal("m", ModelColumn.Resolve(Models, "m", "", "config-model", cache));

        // Neither: the config model.
        Assert.Equal("config-model", ModelColumn.Resolve(Models, "", "", "config-model", cache));
        Assert.Equal("config-model", ModelColumn.Resolve(Models, null, null, "config-model", cache));

        // A server that names the model it really serves wins over the configured alias.
        probe.ModelsJson = """{"data":[{"id":"Qwen/Qwen3-32B"}]}""";
        Assert.Equal("qwen3", ModelColumn.Resolve(Models, "spark", "http://s:8000", "config-model", cache));
    }
}
