using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Config;

/// <summary>
/// 1146-A: the pool's <c>defaultWorker</c>, the type a card with no <c>Worker:</c> line goes to (WT-3),
/// and its migration from the old <c>premium</c> flags when a file names none.
/// </summary>
public class DefaultWorkerTests
{
    private sealed class FakeLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }

    private static SchedulerConfig Load() => SchedulerConfigLoader.Load("{}", @"C:\repos\Proj", @"C:\Users\dev\AppData\Local");

    private static WorkerPool Parse(string types, string extra = "", FakeLog? log = null) =>
        WorkerTypesJson.Parse("{\"pool\":{\"maxWorkers\":4," + extra + "\"types\":{" + types + "}}}", Load(), log ?? new FakeLog())!;

    // Today's pool: Spark is the one type the premium flag left off, so it is the default - the
    // behaviour does not change.
    [Fact]
    public void DefaultWorker_Missing_FirstTypeNotPremium()
    {
        var pool = Parse("""
            "Sonnet": {"max": 1, "premium": true},
            "Spark":  {"max": 2, "premium": false},
            "Opus":   {"max": 1, "premium": true}
            """);

        Assert.Equal("Spark", pool.DefaultWorker);
        Assert.Equal("Spark", pool.DefaultType);
        Assert.True(pool.IsDefault("spark"));
        Assert.False(pool.IsDefault("Sonnet"));
    }

    [Fact]
    public void DefaultWorker_Missing_AllPremium_FirstType()
    {
        var pool = Parse("""
            "Sonnet": {"premium": true},
            "Opus":   {"premium": true}
            """);

        Assert.Equal("Sonnet", pool.DefaultType);
    }

    [Fact]
    public void DefaultWorker_Missing_NoFlags_FirstType()
    {
        var pool = Parse("""
            "Opus": {},
            "Spark": {}
            """);

        Assert.Equal("Opus", pool.DefaultType);
    }

    [Fact]
    public void DefaultWorker_Named_WinsOverPremiumFlags_AnyCase()
    {
        var log = new FakeLog();
        var pool = Parse("""
            "Spark": {"premium": false},
            "Opus":  {"premium": true}
            """, "\"defaultWorker\":\"opus\",", log);

        Assert.Equal("Opus", pool.DefaultType);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void DefaultWorker_NamesNoType_WarnsAndFallsBack()
    {
        var log = new FakeLog();
        var pool = Parse("""
            "Sonnet": {"premium": true},
            "Spark":  {}
            """, "\"defaultWorker\":\"Haiku\",", log);

        Assert.Equal("Spark", pool.DefaultType);
        Assert.Contains(log.Lines, l => l.Contains("defaultWorker Haiku names no worker type") && l.Contains("the default is Spark"));
    }

    // Saving writes defaultWorker and no premium; reading it back gives the same default.
    [Fact]
    public void DefaultWorker_BuildPool_RoundTrip()
    {
        var pool = Parse("""
            "Sonnet": {"premium": true},
            "Spark":  {}
            """);

        var text = WorkerTypesJson.BuildPool(pool);

        Assert.Contains("\"defaultWorker\": \"Spark\"", text);
        Assert.DoesNotContain("premium", text);
        var again = WorkerTypesJson.Parse(text, Load(), new FakeLog());
        Assert.NotNull(again);
        Assert.Equal("Spark", again.DefaultWorker);
        Assert.Equal(["Sonnet", "Spark"], [.. again.Types.Select(t => t.Name)]);
    }

    // A pool whose DefaultWorker names nothing still names a type when written: the first.
    [Fact]
    public void DefaultWorker_UnknownInMemory_BuildPoolWritesFirstType()
    {
        var pool = new WorkerPool
        {
            MaxWorkers = 2,
            Types = [new WorkerType { Name = "a", Max = 1 }, new WorkerType { Name = "b", Max = 1 }],
            DefaultWorker = "zzz",
        };

        Assert.Contains("\"defaultWorker\": \"a\"", WorkerTypesJson.BuildPool(pool));
    }

    // The old slots form: the type of the first slot the premium flag left off.
    [Fact]
    public void DefaultWorker_SlotsForm_FirstSlotNotPremium()
    {
        const string text = """
            {"slots":3,"workers":{
              "1": {"name":"Sonnet","agent":"claude","model":"claude-sonnet-5-5","premium":true},
              "2": {"name":"worker","model":"spark"},
              "3": {"name":"worker","model":"spark"}}}
            """;

        var pool = WorkerTypesJson.FromWorkersJson(Load(), text, null);

        Assert.Equal("worker", pool.DefaultType);
    }
}
