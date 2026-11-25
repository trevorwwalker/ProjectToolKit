// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Host.State;
using Scheduler.Web.Server;

namespace Scheduler.Host.Live;

/// <summary>
/// 1101-B: the live view's source at up to 24 frames a second, on a background thread of its own and never
/// the tick's. Each pass reads the new bytes of every running worker's readable log (from the offset reached
/// last time; back at 0 when the slot's run or card changes), samples the CPU every 250 ms, and builds a
/// <see cref="LiveFrame"/> of only what changed since the previous one. A pass that finds nothing changed
/// builds no frame. The path runs no git: it reads files, the running list and a sample, nothing else.
/// </summary>
public sealed class LiveHub : IDisposable
{
    /// <summary>The gap between frames: 1/24 s.</summary>
    public static readonly TimeSpan FrameGap = TimeSpan.FromSeconds(1d / 24);

    /// <summary>The gap between CPU and memory samples.</summary>
    public static readonly TimeSpan SampleGap = TimeSpan.FromMilliseconds(250);

    /// <summary>How many samples the smoothed CPU is the mean of.</summary>
    public const int SmoothSamples = 4;

    /// <summary>How many lines of a slot <see cref="Full"/> carries.</summary>
    public const int HistoryLines = 20;

    private const int KeptFrames = 64;
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<LiveWorker>> _workers;
    private readonly IHostLoad _load;
    private readonly Func<string> _pageVersion;
    private readonly LiveStreamSource _source;
    private readonly Dictionary<string, SlotState> _slots = [];
    private readonly Queue<double> _recent = new();
    private readonly List<LiveFrame> _frames = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;
    private DateTimeOffset _lastSample = DateTimeOffset.MinValue;
    private HostLoadSample? _latest;
    private LiveFrameLoad? _shownLoad;
    private string _shownPage = "";
    private long _seq;

    /// <summary>Builds a hub over the running workers, a host load sampler and the page snapshot's version.</summary>
    public LiveHub(IClock clock, Func<IReadOnlyList<LiveWorker>> workers, IHostLoad load, Func<string> pageVersion)
    {
        _clock = clock;
        _workers = workers;
        _load = load;
        _pageVersion = pageVersion;
        _source = new LiveStreamSource(() => Seq, FramesAfter, Full);
    }

    /// <summary>Raised on the hub's thread with every frame it builds.</summary>
    public event Action<LiveFrame>? Frame;

    /// <summary>1110-B: this hub as the page server's live source for <c>/live/stream</c> (1102-C).</summary>
    public LiveStreamSource Source => _source;

    /// <summary>The number of the last frame built, 0 before any.</summary>
    public long Seq
    {
        get
        {
            lock (_gate)
            {
                return _seq;
            }
        }
    }

    /// <summary>The latest CPU percent sampled, or null before the first sample.</summary>
    public double? CpuLatest
    {
        get
        {
            lock (_gate)
            {
                return _latest?.CpuPercent;
            }
        }
    }

    /// <summary>The mean of the last <see cref="SmoothSamples"/> CPU samples, or null before the first.</summary>
    public double? CpuSmooth
    {
        get
        {
            lock (_gate)
            {
                return _recent.Count == 0 ? null : _recent.Average();
            }
        }
    }

    /// <summary>Starts the hub's own thread. Calling it again does nothing.</summary>
    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "live-hub" };
        _thread.Start();
    }

    /// <summary>Stops the thread and waits for it.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    /// <summary>The frames after <paramref name="seq"/> that are still kept, oldest first (empty when none are newer).</summary>
    public IReadOnlyList<LiveFrame> FramesAfter(long seq)
    {
        lock (_gate)
        {
            return [.. _frames.Where(f => f.Seq > seq)];
        }
    }

    /// <summary>
    /// Everything the hub shows now, as one frame carrying the current <see cref="Seq"/>: for a client that has
    /// just connected, or has missed frames no longer kept.
    /// </summary>
    public LiveFrame Full()
    {
        lock (_gate)
        {
            return new LiveFrame
            {
                Seq = _seq,
                T = _clock.Now,
                Slots = _slots.ToDictionary(
                    s => s.Key,
                    s => new LiveFrameSlot { Reset = true, Lines = [.. s.Value.Tail], Stats = s.Value.Stats, Time = s.Value.Time }),
                Load = _shownLoad,
                Page = _shownPage.Length == 0 ? null : _shownPage,
            };
        }
    }

    /// <summary>
    /// One pass: sample if 250 ms have passed, read the new log bytes, and return the frame of what changed, or
    /// null when nothing did. The thread calls it every <see cref="FrameGap"/>; a test calls it directly.
    /// </summary>
    public LiveFrame? Step()
    {
        LiveFrame frame;
        lock (_gate)
        {
            var now = _clock.Now;
            Sample(now);
            var changed = new Dictionary<string, LiveFrameSlot>();
            var seen = new HashSet<string>();
            foreach (var w in _workers())
            {
                var key = KeyOf(w);
                _ = seen.Add(key);
                if (ReadSlot(key, w) is { } slot)
                {
                    changed[key] = slot;
                }
            }

            foreach (var gone in _slots.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _ = _slots.Remove(gone);
            }

            var load = ChangedLoad();
            var page = ChangedPage();
            if (changed.Count == 0 && load is null && page is null)
            {
                return null;
            }

            frame = new LiveFrame { Seq = ++_seq, T = now, Slots = changed, Load = load, Page = page };
            _frames.Add(frame);
            if (_frames.Count > KeptFrames)
            {
                _frames.RemoveRange(0, _frames.Count - KeptFrames);
            }
        }

        Frame?.Invoke(frame);
        return frame;
    }

    private static string KeyOf(LiveWorker w) =>
        w.Project.Length == 0 ? w.Slot.ToString(CultureInfo.InvariantCulture) : w.Project + ":" + w.Slot.ToString(CultureInfo.InvariantCulture);

    private void Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                _ = Step();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // a pass that fails is left for the next pass to do again
            }

            var wait = FrameGap - (DateTimeOffset.UtcNow - started);
            if (wait > TimeSpan.Zero && _stop.Token.WaitHandle.WaitOne(wait))
            {
                return;
            }
        }
    }

    private void Sample(DateTimeOffset now)
    {
        if (_latest is not null && now - _lastSample < SampleGap)
        {
            return;
        }

        _lastSample = now;
        _latest = _load.Sample();
        _recent.Enqueue(_latest.CpuPercent);
        while (_recent.Count > SmoothSamples)
        {
            _ = _recent.Dequeue();
        }
    }

    private LiveFrameLoad? ChangedLoad()
    {
        if (_latest is not { } s)
        {
            return null;
        }

        var shown = new LiveFrameLoad(
            HostLoad.ShownCpuPercent(s),
            HostLoad.ShownCpuPercent(new HostLoadSample { CpuPercent = _recent.Average() }),
            s.FreeMemoryBytes,
            s.TotalMemoryBytes);
        if (shown == _shownLoad)
        {
            return null;
        }

        _shownLoad = shown;
        return shown;
    }

    private string? ChangedPage()
    {
        var version = _pageVersion();
        if (version == _shownPage)
        {
            return null;
        }

        _shownPage = version;
        return version;
    }

    // What changed in one running worker since the last pass, or null when nothing did. The offset belongs to the
    // run: a new log file name (the card or the run changed) starts it again at 0.
    private LiveFrameSlot? ReadSlot(string key, LiveWorker w)
    {
        var run = Path.GetFileName(w.LogPath);
        var fresh = !_slots.TryGetValue(key, out var found) || found.Run != run;
        var state = fresh ? new SlotState(run) : found!;
        if (fresh)
        {
            _slots[key] = state;
        }

        var lines = ReadNew(w.LogPath, state);
        if (fresh && lines.Count > HistoryLines)
        {
            lines = lines[^HistoryLines..];
        }

        var stats = fresh || state.Stats != w.Stats ? w.Stats : null;
        var time = fresh || state.Time != w.Time ? w.Time : null;
        if (!fresh && lines.Count == 0 && stats is null && time is null)
        {
            return null;
        }

        state.Stats = w.Stats;
        state.Time = w.Time;
        state.Tail.AddRange(lines);
        if (state.Tail.Count > HistoryLines)
        {
            state.Tail.RemoveRange(0, state.Tail.Count - HistoryLines);
        }

        return new LiveFrameSlot { Reset = fresh, Lines = lines, Stats = stats, Time = time };
    }

    // The whole lines after the slot's offset, moving the offset past them; a line still being written waits.
    private static List<string> ReadNew(string path, SlotState state)
    {
        try
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                return [];
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < state.Offset)
            {
                state.Offset = 0; // the file was started again
            }

            if (fs.Length == state.Offset)
            {
                return [];
            }

            _ = fs.Seek(state.Offset, SeekOrigin.Begin);
            var buf = new byte[fs.Length - state.Offset];
            var n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            var take = n == 0 ? 0 : Array.LastIndexOf(buf, (byte)'\n', n - 1) + 1;
            if (take <= 0)
            {
                return [];
            }

            state.Offset += take;
            return [.. Utf8.GetString(buf, 0, take).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'))];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private sealed class SlotState(string run)
    {
        public string Run { get; } = run;

        public long Offset { get; set; }

        public string Stats { get; set; } = "";

        public string Time { get; set; } = "";

        public List<string> Tail { get; } = [];
    }
}
