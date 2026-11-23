using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// 1102-C: where the stream reads its frames from; the Host wires the live hub (1101-B) in through these three
/// members, so the page server needs no reference to the Host.
/// </summary>
/// <param name="Seq">The number of the last frame built.</param>
/// <param name="FramesAfter">The kept frames after a number, oldest first.</param>
/// <param name="Full">Everything shown now, as one frame carrying the current number.</param>
public sealed record LiveStreamSource(
    Func<long> Seq,
    Func<long, IReadOnlyList<LiveFrame>> FramesAfter,
    Func<LiveFrame> Full);

/// <summary>
/// 1102-C: one client of <c>GET /live/stream</c> (Server-Sent Events). It pulls from the hub no faster than 24
/// times a second and sends the frames since the last one it sent as one coalesced event, so a slow client
/// never makes a queue: while a write blocks, nothing is held but a number. A client that is too far behind
/// for the kept frames (or whose <c>Last-Event-ID</c> is not one the hub has) gets a full frame instead.
/// </summary>
public sealed class LiveStream(LiveStreamSource source, string? project = null)
{
    /// <summary>The shortest gap between two events: 1/24 s.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(1d / 24);

    /// <summary>How long a quiet stream goes before a comment line is sent.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The response head of a stream.</summary>
    public static string Head =>
        "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\n"
        + "X-Accel-Buffering: no\r\nConnection: close\r\n\r\n";

    /// <summary>Whether a request is for the stream.</summary>
    public static bool Wants(PageRequest request) =>
        string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) && request.Path == "/live/stream";

    /// <summary>
    /// Writes the head and then events until the client goes (a write throws) or <paramref name="stop"/> is
    /// cancelled. <paramref name="lastEventId"/> is the request's <c>Last-Event-ID</c> header, if any;
    /// <paramref name="keepAlive"/> is the quiet time before a comment line.
    /// </summary>
    public void Run(Stream output, string? lastEventId, CancellationToken stop, TimeSpan? keepAlive = null)
    {
        var quiet = keepAlive ?? KeepAlive;
        Write(output, Head);
        var sent = long.TryParse(lastEventId, out var id) && id >= 0 ? id : -1;
        var lastWrite = DateTime.UtcNow;
        while (!stop.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            var frame = Next(ref sent);
            if (frame is not null)
            {
                Event(output, frame);
                lastWrite = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastWrite >= quiet)
            {
                Write(output, ": keep-alive\n\n");
                lastWrite = DateTime.UtcNow;
            }

            var wait = MinGap - (DateTime.UtcNow - started);
            if (wait > TimeSpan.Zero && stop.WaitHandle.WaitOne(wait))
            {
                return;
            }
        }
    }

    /// <summary>
    /// The one frame to send now, or null when there is nothing new. <paramref name="sent"/> is the number of the
    /// last frame this client has (-1 for none) and moves to the number of the frame returned.
    /// </summary>
    public LiveFrame? Next(ref long sent)
    {
        var now = source.Seq();
        if (sent < 0)
        {
            var full = source.Full();
            sent = full.Seq;
            return Filter(full, always: true);
        }

        if (now == sent)
        {
            return null;
        }

        if (now < sent)
        {
            // a client ahead of the hub (the scheduler restarted): start it again
            var reset = source.Full();
            sent = reset.Seq;
            return Filter(reset, always: true);
        }

        var frames = source.FramesAfter(sent);
        if (frames.Count == 0 || frames[0].Seq != sent + 1)
        {
            var full = source.Full();
            sent = full.Seq;
            return Filter(full, always: true);
        }

        sent = frames[^1].Seq;
        return Filter(Merge(frames), always: false);
    }

    // One frame of the many: lines add up, a reset drops what came before it, the latest value of a part wins.
    private static LiveFrame Merge(IReadOnlyList<LiveFrame> frames)
    {
        if (frames.Count == 1)
        {
            return frames[0];
        }

        var slots = new Dictionary<string, LiveFrameSlot>();
        LiveFrameLoad? load = null;
        string? page = null;
        foreach (var f in frames)
        {
            foreach (var (key, s) in f.Slots)
            {
                slots[key] = slots.TryGetValue(key, out var before)
                    ? new LiveFrameSlot
                    {
                        Reset = before.Reset || s.Reset,
                        Lines = s.Reset ? s.Lines : [.. before.Lines, .. s.Lines],
                        Stats = s.Stats ?? before.Stats,
                        Time = s.Time ?? before.Time,
                    }
                    : s;
            }

            load = f.Load ?? load;
            page = f.Page ?? page;
        }

        return new LiveFrame { Seq = frames[^1].Seq, T = frames[^1].T, Slots = slots, Load = load, Page = page };
    }

    // A project's stream keeps only its slots (keyed Project:n); a frame left with nothing is not sent.
    private LiveFrame? Filter(LiveFrame frame, bool always)
    {
        var kept = frame;
        if (!string.IsNullOrEmpty(project))
        {
            var prefix = project + ":";
            kept = frame with
            {
                Slots = frame.Slots
                    .Where(s => s.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(s => s.Key, s => s.Value),
            };
        }

        return always || kept.Slots.Count > 0 || kept.Load is not null || kept.Page is not null ? kept : null;
    }

    private static void Event(Stream output, LiveFrame frame) =>
        Write(output, "id: " + frame.Seq + "\ndata: " + JsonSerializer.Serialize(frame, Json) + "\n\n");

    private static void Write(Stream output, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
        output.Flush();
    }
}
