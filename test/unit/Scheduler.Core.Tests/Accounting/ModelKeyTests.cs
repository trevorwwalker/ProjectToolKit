// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

public class ModelKeyTests
{
    private static readonly ModelEntry[] Models =
    [
        new() { Key = "qwen3-32b", Source = "Qwen/Qwen3-32B-AWQ" },
        new() { Key = "opus" },
    ];

    private const string ServedRoot = """{"object":"list","data":[{"id":"/models/Qwen3-32B-AWQ","object":"model","max_model_len":32768}]}""";

    private static (FakeClock Clock, FakeServerProbe Probe, ServerModelCache Cache) Server(string? modelsJson)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.FromHours(-8)));
        var probe = new FakeServerProbe { ModelsJson = modelsJson };
        return (clock, probe, new ServerModelCache(clock, probe));
    }

    // ACCT-3 (Model-Key, schedule.ps1 L2263): the models.json entry a worker's model resolves to.
    [Fact]
    public void Acct_ModelKey_Resolution()
    {
        var (clock, probe, cache) = Server(ServedRoot);

        // The model name itself first: an exact key needs no server.
        Assert.Equal("opus", ModelKey.Resolve(Models, "opus", "http://s:8000", cache));
        Assert.Equal(0, probe.ModelsFetches);

        // An alias: the server's root, matched by its last path part against the source's last part.
        Assert.Equal("qwen3-32b", ModelKey.Resolve(Models, "spark", "http://s:8000", cache));

        // Nothing the server serves identifies the model: no name, no prefix, so ''.
        var (_, _, other) = Server("""{"data":[{"id":"/models/SomeOther-7B"}]}""");
        Assert.Equal("", ModelKey.Resolve(Models, "gpt", "http://s:8000", other));
        Assert.Equal("", ModelKey.Resolve(Models, "qwen3-32b-instruct", "http://s:8000", other));

        // The root's last part matches no name but starts with keys: the longest of them.
        var (_, _, byPrefix) = Server("""{"data":[{"id":"/models/Qwen3-32b-Instruct-4k"}]}""");
        Assert.Equal("qwen3-32b", ModelKey.Resolve(Models, "qwen3-32b-instruct", "http://s:8000", byPrefix));
        ModelEntry[] longer = [.. Models, new() { Key = "qwen3-32b-instruct", Source = "Qwen/Qwen3-32B-Instruct" }];
        Assert.Equal("qwen3-32b-instruct", ModelKey.Resolve(longer, "any", "http://s:8000", byPrefix));

        // A model that is a key matches whatever the server says, and no model at all is ''.
        Assert.Equal("qwen3-32b", ModelKey.Resolve(Models, "qwen3-32b", "http://s:8000", byPrefix));
        Assert.Equal("", ModelKey.Resolve(Models, "", "http://s:8000", cache));
        Assert.Equal("", ModelKey.Resolve(Models, null, "http://s:8000", cache));

        // A server that does not answer resolves nothing but the exact keys.
        var (_, _, down) = Server(null);
        Assert.Equal("", ModelKey.Resolve(Models, "spark", "http://s:8000", down));
        Assert.Equal("opus", ModelKey.Resolve(Models, "opus", "http://s:8000", down));

        // /models is asked at most every 10 minutes per endpoint.
        var (tick, asked, timed) = Server(ServedRoot);
        Assert.Equal("qwen3-32b", ModelKey.Resolve(Models, "spark", "http://s:8000", timed));
        Assert.Equal(1, asked.ModelsFetches);
        tick.Advance(TimeSpan.FromMinutes(9));
        ModelKey.Resolve(Models, "spark", "http://s:8000", timed);
        Assert.Equal(1, asked.ModelsFetches);
        tick.Advance(TimeSpan.FromMinutes(1));
        ModelKey.Resolve(Models, "spark", "http://s:8000", timed);
        Assert.Equal(2, asked.ModelsFetches);

        // A key is matched without regard to case, and comes back as models.json spells it.
        Assert.Equal(2, asked.ModelsFetches);
        Assert.Equal("opus", ModelKey.Resolve(Models, "OPUS", "http://s:8000", timed));
        Assert.Equal(2, asked.ModelsFetches);
    }
}
