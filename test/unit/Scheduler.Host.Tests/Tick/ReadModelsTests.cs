// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// 1211-B (known gap 285-B (b)): <c>TickHost.ReadModels</c> reads the entries under models.json's <c>models</c>
/// object (key -> short, name, source and every served alias); the top-level <c>about</c> is never a model, and an
/// old flat file is still read as before.
/// </summary>
[ParallelMethods]
public class ReadModelsTests
{
    private const string Real = """
        {"about":"What each model can do.","models":{
          "claude-opus-5-5":{"short":"opus","name":"Claude Opus 5.5","source":"anthropic/opus","served":["spark","opus-alias"]},
          "qwen":{"short":"qwen","name":"Qwen 3","source":"Qwen/Qwen3","served":[]}}}
        """;

    [Fact]
    public void RealShape_TwoEntries_GiveTheirKeysAndNames()
    {
        var models = Read(Real, out _);
        Assert.Equal(["claude-opus-5-5", "qwen"], models.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["opus", "Claude Opus 5.5", "anthropic/opus", "spark", "opus-alias"], models["claude-opus-5-5"]);
        Assert.Equal(["qwen", "Qwen 3", "Qwen/Qwen3"], models["qwen"]);
        Assert.False(models.ContainsKey("about"));
        Assert.False(models.ContainsKey("models"));
    }

    [Fact]
    public void EmptyModels_GivesNone()
    {
        Assert.Empty(Read("""{"about":"x","models":{}}""", out _));
    }

    [Fact]
    public void FlatFile_IsReadAsBefore()
    {
        var models = Read("""{"Opus":{"short":"opus"}}""", out _);
        Assert.Equal(["opus"], models["Opus"]);
    }

    [Fact]
    public void Malformed_GivesNone_AndLogsALine()
    {
        var models = Read("{ not json", out var log);
        Assert.Empty(models);
        Assert.Contains(log, l => l.Contains("models.json", StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Read(string modelsJson, out string[] log)
    {
        var cards = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"jobs/queue/{TickRig.CardName}.md"] = TickRig.CardText,
            ["jobs/models.json"] = modelsJson,
        };
        using var rig = new TickRig("echo x\r\n", cards: cards);
        rig.TickUntilSettled(() => false, ticks: 2);
        var models = rig.Composition.Host.ReadModels();
        log = [.. rig.Log.Lines];
        return models;
    }
}
