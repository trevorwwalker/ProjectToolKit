using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Scheduler.Core.Dispatch;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.State;

/// <summary>
/// The model server probe (ACCT-8, <c>Server-Up</c> L1005): <c>GET &lt;endpoint&gt;/models</c>
/// with a 5 s timeout, the answer cached 20 s, and a first "down" and every change logged. The
/// server is a real one on the loopback interface. Test plan: accounting.md.
/// </summary>
public class ServerProbeTests
{
    // ACCT-8: down after the 5 s timeout with a first "down" logged; cached 20 s; "up" logged on
    // the change; no endpoint is up without a request.
    [Fact]
    public void Acct_ServerUp_TimeoutCacheAndLogging()
    {
        using var server = new FakeModelServer();
        var log = new ListLog();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var cache = new ServerCache(new ServerProbe(), clock, log);

        server.Hang = TimeSpan.FromSeconds(10);
        var watch = Stopwatch.StartNew();
        Assert.False(cache.IsUp(server.Url));
        watch.Stop();
        Assert.True(
            watch.Elapsed >= TimeSpan.FromSeconds(4) && watch.Elapsed < TimeSpan.FromSeconds(9),
            $"the probe waited {watch.Elapsed.TotalSeconds:0.0} s, not the 5 s timeout");
        Assert.Contains(log.Lines, l => l.Contains("does not answer", StringComparison.Ordinal));
        Assert.Equal(1, server.Requests);

        // The answer is cached for 20 s: the same "down", and no request reaches the server.
        server.Hang = TimeSpan.Zero;
        Assert.False(cache.IsUp(server.Url));
        Assert.Equal(1, server.Requests);

        // Once the 20 seconds are past the server is asked again, and the change to "up" is logged.
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(cache.IsUp(server.Url));
        Assert.Equal(2, server.Requests);
        Assert.Contains(log.Lines, l => l.Contains("answers again", StringComparison.Ordinal));

        // No endpoint means up, and nothing is asked.
        Assert.True(cache.IsUp(""));
        Assert.Equal(2, server.Requests);
    }

    // ACCT-3 and ACCT-7 read through the same probe: the /models and metrics text, or null when
    // the server does not answer.
    [Fact]
    public void Acct_ServerProbe_ModelsAndMetricsText()
    {
        using var server = new FakeModelServer();
        var probe = new ServerProbe();
        Assert.Equal("""{"data":[{"id":"gpt-4"}]}""", probe.ReadModels(server.Url));
        Assert.Equal("vllm:num_requests_running 1", probe.ReadMetrics(server.Url + "/metrics"));
        Assert.Null(probe.ReadModels("http://127.0.0.1:1/"));
        Assert.Null(probe.ReadMetrics("http://127.0.0.1:1/metrics"));
    }

    /// <summary>A small HTTP server on 127.0.0.1 that can be told to hang before it answers.</summary>
    private sealed class FakeModelServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); // loopback only: binding every interface makes Windows Firewall ask about each slot's testhost.exe
        private readonly List<TcpClient> _open = [];

        public FakeModelServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(AcceptLoop);
        }

        public string Url { get; }

        /// <summary>How long a request is held before the server answers it.</summary>
        public TimeSpan Hang { get; set; }

        /// <summary>Requests the server has taken, whatever became of them.</summary>
        public int Requests;

        private async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return; // the listener was stopped
                }

                lock (_open)
                {
                    _open.Add(client);
                }

                _ = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[4096];
                    var read = 0;
                    while (read < buffer.Length)
                    {
                        var n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0)
                        {
                            break;
                        }

                        read += n;
                        if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
                        {
                            break;
                        }
                    }

                    Interlocked.Increment(ref Requests);
                    if (Hang > TimeSpan.Zero)
                    {
                        Thread.Sleep(Hang);
                    }

                    var path = ExtractPath(buffer, read);
                    var body = path.Contains("metrics", StringComparison.OrdinalIgnoreCase)
                        ? "vllm:num_requests_running 1"
                        : """{"data":[{"id":"gpt-4"}]}""";
                    var bytes = Encoding.ASCII.GetBytes(body);
                    var header = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            catch (Exception)
            {
                // The client gave up on us, which is exactly what the first test measures.
            }
            finally
            {
                lock (_open)
                {
                    _ = _open.Remove(client);
                }
            }
        }

        private static string ExtractPath(byte[] buffer, int read)
        {
            var text = Encoding.ASCII.GetString(buffer, 0, Math.Max(0, read));
            var start = text.IndexOf(' ');
            if (start < 0)
            {
                return "";
            }

            var end = text.IndexOf(' ', start + 1);
            return end < 0 ? text[(start + 1)..] : text[(start + 1)..end];
        }

        public void Dispose()
        {
            _listener.Stop();
            lock (_open)
            {
                foreach (var client in _open)
                {
                    client.Dispose();
                }

                _open.Clear();
            }
        }
    }
}
