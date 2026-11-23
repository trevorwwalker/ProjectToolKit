using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Scheduler.Host.Config;
using Scheduler.Host.Pool;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// 1110-B: the pool composition gives its page server the live hub as <c>/live/stream</c>'s source (1102-C),
/// so an open page is answered with frames instead of the route being refused.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class PoolLiveStreamTests
{
    // A hang here becomes a failing assert; the caps are generous on purpose (as in PoolParallelStartTests).
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan FirstFrame = TimeSpan.FromSeconds(30);

    // The pool's page answers /live/stream: a text/event-stream head, then the hub's current frame.
    [Fact]
    public async Task MP_L1_PoolPageLiveStream_AnswersWithFirstHubFrame()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var gate = new ManualResetEventSlim();
        ConfigLoadResult Held(string repo, Func<string, ConfigLoadResult> load)
        {
            _ = gate.Wait(Long);
            return load(repo);
        }

        // The pool is composed and run on a thread of its own, as the program does; its joins stay behind the
        // gate, so what is measured here is the page and the hub and not a running worker.
        using var stop = new CancellationTokenSource();
        using var composed = new ManualResetEventSlim();
        PoolComposition? pool = null;
        Exception? failed = null;
        var runner = new Thread(() =>
        {
            try
            {
                using var p = rig.Compose(wait: false, load: Held, dryRun: false);
                pool = p;
                composed.Set();
                p.Runner.Run(stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // the runner's wait ends a cancelled run this way (as in PoolParallelStartTests)
            }
            catch (Exception e)
            {
                failed = e;
                composed.Set();
            }
        })
        { IsBackground = true };
        runner.Start();
        try
        {
            Assert.True(composed.Wait(Long));
            Assert.Null(failed);
            var hub = pool!.Hub;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var response = await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{pool.Config.Port}/live/stream"),
                HttpCompletionOption.ResponseHeadersRead).WaitAsync(Long);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

            using var body = new StreamReader(await response.Content.ReadAsStreamAsync());
            var id = await body.ReadLineAsync().WaitAsync(FirstFrame);
            var data = await body.ReadLineAsync().WaitAsync(FirstFrame);
            Assert.NotNull(id);
            Assert.NotNull(data);
            Assert.StartsWith("id: ", id, StringComparison.Ordinal);
            Assert.StartsWith("data: ", data, StringComparison.Ordinal);

            // The frame came from the hub: the event's number is the hub's own and the JSON agrees with it.
            var seq = long.Parse(id[4..], CultureInfo.InvariantCulture);
            using var frame = JsonDocument.Parse(data[6..]);
            Assert.Equal(seq, frame.RootElement.GetProperty("seq").GetInt64());
            Assert.InRange(seq, 0, hub.Seq); // the hub is at or past the frame this client was given
        }
        finally
        {
            gate.Set();
            stop.Cancel();
            Assert.True(runner.Join(Long));
        }
    }
}
