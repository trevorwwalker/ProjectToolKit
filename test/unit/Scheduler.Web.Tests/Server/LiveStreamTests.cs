using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;

namespace Scheduler.Web.Tests.Server;

// 1102-C: GET /live/stream, the hub frames as Server-Sent Events.
public class LiveStreamTests
{
    [Fact]
    public void Next_NothingChanged_SendsNothing_ThenOneFramePerChange()
    {
        var hub = new FakeHub();
        var stream = new LiveStream(hub.Source);
        var sent = -1L;

        Assert.NotNull(stream.Next(ref sent)); // a new client gets everything first
        Assert.Null(stream.Next(ref sent));
        Assert.Null(stream.Next(ref sent));

        hub.Add("4", "a");
        var frame = stream.Next(ref sent);
        Assert.Equal(1, frame?.Seq);
        Assert.Null(stream.Next(ref sent));
    }

    [Fact]
    public void Next_LastEventId_ResumesWithTheMissedFramesCoalesced()
    {
        var hub = new FakeHub();
        for (var i = 1; i <= 5; i++)
        {
            hub.Add("4", "line" + i);
        }

        var sent = 2L;
        var frame = new LiveStream(hub.Source).Next(ref sent);

        Assert.Equal(5, sent);
        Assert.Equal(["line3", "line4", "line5"], frame!.Slots["4"].Lines);
    }

    [Fact]
    public void Next_TooFarBehind_GetsAFullFrame()
    {
        var hub = new FakeHub { Kept = 3 };
        for (var i = 1; i <= 10; i++)
        {
            hub.Add("4", "line" + i);
        }

        var sent = 2L; // frames 3..7 are gone
        var frame = new LiveStream(hub.Source).Next(ref sent);

        Assert.Equal(10, sent);
        Assert.True(frame!.Slots["4"].Reset);
    }

    [Fact]
    public void Next_Project_KeepsOnlyThatProjectsSlots_AndSkipsAFrameLeftEmpty()
    {
        var hub = new FakeHub();
        var stream = new LiveStream(hub.Source, "Alpha");
        var sent = -1L;
        _ = stream.Next(ref sent);

        hub.Add("Beta:1", "x");
        Assert.Null(stream.Next(ref sent));

        hub.Add("Alpha:2", "y");
        var frame = stream.Next(ref sent);
        Assert.Equal(["Alpha:2"], frame!.Slots.Keys);
    }

    [Fact]
    public async Task Run_NeverSendsMoreThan24EventsASecond()
    {
        var hub = new FakeHub();
        using var cts = new CancellationTokenSource();
        var output = new CountingStream();
        var run = Task.Run(() => new LiveStream(hub.Source).Run(output, null, cts.Token));
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 1000)
        {
            hub.Add("4", "x"); // a change every millisecond or so
            Thread.Sleep(1);
        }

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(output.Events, 5, 26); // the first full frame plus at most 24 in the second
    }

    [Fact]
    public async Task Run_SlowClient_GetsOneCoalescedEventNotAQueue()
    {
        var hub = new FakeHub();
        using var cts = new CancellationTokenSource();
        using var gate = new ManualResetEventSlim(false);
        var output = new CountingStream { Gate = gate };
        var run = Task.Run(() => new LiveStream(hub.Source).Run(output, null, cts.Token));
        _ = SpinWait.SpinUntil(() => output.Writing, TimeSpan.FromSeconds(5)); // stuck inside the first write

        for (var i = 0; i < 2000; i++)
        {
            hub.Add("4", "x");
        }

        gate.Set();
        _ = SpinWait.SpinUntil(() => output.LastId == 2000, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2000, output.LastId);
        Assert.InRange(output.Events, 2, 4); // the first event and the catch-up, not 2000
    }

    [Fact]
    public async Task Run_QuietStream_SendsACommentLine()
    {
        var hub = new FakeHub();
        using var cts = new CancellationTokenSource();
        var output = new CountingStream();
        var run = Task.Run(() => new LiveStream(hub.Source).Run(output, null, cts.Token, TimeSpan.FromMilliseconds(100)));
        _ = SpinWait.SpinUntil(() => output.Text.Contains(": keep-alive", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(": keep-alive\n\n", output.Text, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/event-stream", output.Text, StringComparison.Ordinal);
        Assert.Contains("Cache-Control: no-cache", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_StreamOpen_OtherRequestsAreStillServed_AndResumeWorks()
    {
        using var web = new WebHarness();
        var hub = new FakeHub();
        hub.Add("4", "one");
        hub.Add("4", "two");
        web.Server.LiveSource = hub.Source;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/live/stream");
        request.Headers.Add("Last-Event-ID", "1");
        using var response = await web.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());

        using var body = new StreamReader(await response.Content.ReadAsStreamAsync());
        var id = await body.ReadLineAsync();
        var data = await body.ReadLineAsync();
        Assert.Equal("id: 2", id);
        Assert.Contains("two", data);
        Assert.DoesNotContain("one", data);

        var other = await web.Client.GetAsync("/live.json");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        hub.Add("4", "three");
        _ = await body.ReadLineAsync(); // the blank line closing the first event
        Assert.Equal("id: 3", await body.ReadLineAsync());
    }

    [Fact]
    public async Task Server_NoSource_NotFound()
    {
        using var web = new WebHarness();
        var response = await web.Client.GetAsync("/live/stream");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FakeHub
    {
        private readonly List<LiveFrame> _frames = [];
        private readonly Lock _gate = new();

        public FakeHub() => Source = new LiveStreamSource(() => Seq, After, Full);

        public int Kept { get; init; } = 64;

        public LiveStreamSource Source { get; }

        private long Seq
        {
            get
            {
                lock (_gate)
                {
                    return _frames.Count == 0 ? 0 : _frames[^1].Seq;
                }
            }
        }

        public void Add(string slot, string line)
        {
            lock (_gate)
            {
                _frames.Add(new LiveFrame
                {
                    Seq = Seq + 1,
                    T = DateTimeOffset.UnixEpoch,
                    Slots = new Dictionary<string, LiveFrameSlot> { [slot] = new LiveFrameSlot { Lines = [line] } },
                });
                if (_frames.Count > Kept)
                {
                    _frames.RemoveAt(0);
                }
            }
        }

        private List<LiveFrame> After(long seq)
        {
            lock (_gate)
            {
                return [.. _frames.Where(f => f.Seq > seq)];
            }
        }

        private LiveFrame Full()
        {
            lock (_gate)
            {
                return new LiveFrame
                {
                    Seq = Seq,
                    T = DateTimeOffset.UnixEpoch,
                    Slots = new Dictionary<string, LiveFrameSlot> { ["4"] = new LiveFrameSlot { Reset = true } },
                };
            }
        }
    }

    // The bytes a client would read, counted by event; a gate holds the first event write.
    private sealed class CountingStream : Stream
    {
        private readonly StringBuilder _text = new();
        private int _events;
        private long _lastId;
        private volatile bool _writing;

        public ManualResetEventSlim? Gate { get; init; }

        public bool Writing => _writing;

        public int Events => Volatile.Read(ref _events);

        public long LastId => Interlocked.Read(ref _lastId);

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var s = Encoding.UTF8.GetString(buffer, offset, count);
            if (s.StartsWith("id: ", StringComparison.Ordinal))
            {
                _writing = true;
                Gate?.Wait(TimeSpan.FromSeconds(10));
                _ = Interlocked.Increment(ref _events);
                _ = Interlocked.Exchange(ref _lastId, long.Parse(s[4..s.IndexOf('\n')], System.Globalization.CultureInfo.InvariantCulture));
            }

            lock (_text)
            {
                _ = _text.Append(s);
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
