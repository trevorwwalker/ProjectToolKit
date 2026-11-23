using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Live;
using Scheduler.Host.Tests.State;
using Scheduler.Web.Server;

namespace Scheduler.Host.Tests.Live;

// 1115-B: LiveHub_Runs_StartsNoGitProcess compares GitRunner.Total, a count over the whole test process, before
// and after; any git another class starts in that window (the composed-program tests run many) moved it by one and
// turned main red on 76a10804. So this class runs alone, after the parallel ones, where only its own code runs.
[CollectionDefinition(nameof(LiveHubTests), DisableParallelization = true)]
public sealed class LiveHubTestsCollection;

[Collection(nameof(LiveHubTests))]
public sealed class LiveHubTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "livehub-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly Load _load = new();
    private string _logName = "1101-B-live-hub-20261122-120000.log";
    private string _stats = "stats";
    private string _time = "1/90 min";
    private string _version = "v1";

    public LiveHubTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string LogPath => Path.Combine(_dir, _logName);

    private LiveHub Hub() => new(
        _clock,
        () => [new LiveWorker(1, LogPath, _stats, _time)],
        _load,
        () => _version);

    private void Append(string text) => File.AppendAllText(LogPath, text);

    // 1101-B: a log appended in steps yields only the new lines, from the offset reached last time.
    [Fact]
    public void LiveHub_LogAppendedInSteps_YieldsOnlyNewLines()
    {
        using var hub = Hub();
        Append("one\r\ntwo\r\n");
        var first = hub.Step();
        Assert.Equal(["one", "two"], first!.Slots["1"].Lines);

        Append("three\r\n");
        var second = hub.Step();
        Assert.Equal(["three"], second!.Slots["1"].Lines);
        Assert.False(second.Slots["1"].Reset);
        Assert.Null(second.Slots["1"].Stats);
        Assert.Equal(first.Seq + 1, second.Seq);
    }

    // A line still being written waits for its line end.
    [Fact]
    public void LiveHub_PartialLine_WaitsForItsLineEnd()
    {
        using var hub = Hub();
        Append("done\r\npart");
        Assert.Equal(["done"], hub.Step()!.Slots["1"].Lines);
        Append("ial\r\n");
        Assert.Equal(["partial"], hub.Step()!.Slots["1"].Lines);
    }

    // A card change (a new log file) starts the slot's offset at 0 again.
    [Fact]
    public void LiveHub_CardChange_ResetsOffset()
    {
        using var hub = Hub();
        Append("old one\r\nold two\r\n");
        _ = hub.Step();

        _logName = "1102-C-stream-20261122-121000.log";
        Append("new one\r\n");
        var frame = hub.Step();
        Assert.True(frame!.Slots["1"].Reset);
        Assert.Equal(["new one"], frame.Slots["1"].Lines);
    }

    // An unchanged state produces no frame.
    [Fact]
    public void LiveHub_NothingChanged_NoFrame()
    {
        using var hub = Hub();
        Append("a\r\n");
        Assert.NotNull(hub.Step());
        var seq = hub.Seq;
        Assert.Null(hub.Step());
        _clock.Advance(LiveHub.SampleGap);
        Assert.Null(hub.Step()); // sampled again, the same value
        Assert.Equal(seq, hub.Seq);
    }

    // A CPU change produces a frame with the load, and the smoothed value is the mean of the last 4 samples.
    [Fact]
    public void LiveHub_CpuChange_ProducesFrame()
    {
        using var hub = Hub();
        _ = hub.Step();
        _load.Cpu = 40;
        Assert.Null(hub.Step()); // under 250 ms: not sampled yet
        _clock.Advance(LiveHub.SampleGap);
        var frame = hub.Step();
        Assert.Equal(40, frame!.Load!.Cpu);
        Assert.Equal(20, frame.Load.CpuSmooth);
        Assert.Empty(frame.Slots);
        Assert.Null(frame.Page);
        Assert.Equal(20, hub.CpuSmooth);
        Assert.Equal(40, hub.CpuLatest);
    }

    // Stats, minutes and the page version go out when they move, and only then.
    [Fact]
    public void LiveHub_StatsTimeAndPageVersion_GoOutWhenTheyMove()
    {
        using var hub = Hub();
        _ = hub.Step();
        _stats = "more stats";
        _time = "2/90 min";
        var frame = hub.Step();
        Assert.Equal("more stats", frame!.Slots["1"].Stats);
        Assert.Equal("2/90 min", frame.Slots["1"].Time);
        Assert.Empty(frame.Slots["1"].Lines);

        _version = "v2";
        Assert.Equal("v2", hub.Step()!.Page);
    }

    // A client that missed frames asks for those after its number; a new one takes the whole state.
    [Fact]
    public void LiveHub_FramesAfterAndFull_CarryWhatAClientNeeds()
    {
        using var hub = Hub();
        Append("a\r\n");
        _ = hub.Step();
        Append("b\r\n");
        _ = hub.Step();
        Assert.Equal([2L], hub.FramesAfter(1).Select(f => f.Seq));
        var full = hub.Full();
        Assert.Equal(2, full.Seq);
        Assert.Equal(["a", "b"], full.Slots["1"].Lines);
        Assert.Equal("v1", full.Page);
    }

    // The hub never starts a git process, and its thread builds frames of its own.
    [Fact]
    public void LiveHub_Runs_StartsNoGitProcess()
    {
        var before = GitRunner.Total;
        var seen = new ManualResetEventSlim();
        using (var hub = Hub())
        {
            hub.Frame += _ => seen.Set();
            Append("x\r\n");
            hub.Start();
            Assert.True(seen.Wait(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(before, GitRunner.Total);
    }

    private sealed class Load : IHostLoad
    {
        public double Cpu { get; set; }

        public HostLoadSample Sample() => new() { CpuPercent = Cpu, FreeMemoryBytes = 1000, TotalMemoryBytes = 2000, Cores = 4 };
    }
}
