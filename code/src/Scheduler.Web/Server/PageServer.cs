// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Scheduler.Web.Server;

/// <summary>
/// The page's listener (PAGE-1): its own thread that accepts requests and answers the ones a
/// snapshot can answer, while every POST goes to the scheduler thread through
/// <see cref="IPageActions"/>. Each request is handled on the thread pool, so a control that has
/// to wait for a busy loop never holds up the page. It is written on a plain socket rather than
/// <c>HttpListener</c> so it can take a free port without a URL reservation.
/// </summary>
public sealed class PageServer : IDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;

    private readonly PageServerOptions _options;
    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _listening;
    private readonly CancellationTokenSource _closing = new();

    /// <summary>A listener that is not started yet.</summary>
    public PageServer(PageServerOptions options)
    {
        _options = options;
        Queue = options.Queue ?? new PageActionQueue();
        OwnsQueue = options.Queue is null;

        // The feed answers with the state of this server's queue, so the page can tell a click that
        // has landed from one still waiting (957-B).
        options.Feed.Actions ??= Queue;
        Router = new PageRouter(options.Data, options.Actions, options.Renderer, options.Feed, options.RemoteControl, Queue, options.Pool);
    }

    /// <summary>The route table this server answers with.</summary>
    public PageRouter Router { get; }

    /// <summary>The worker the POST actions run on: clicks are answered at once and their work is
    /// done here, in order, one at a time (957-B).</summary>
    public PageActionQueue Queue { get; }

    /// <summary>Whether this server made the queue and so has to stop it.</summary>
    private bool OwnsQueue { get; }

    /// <summary>The port it is listening on; the real one when it was started with port 0.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Replaces the address a request came from. It exists so a test can ask what a request from
    /// another PC would be answered (PAGE-7); nothing sets it in the running scheduler.
    /// </summary>
    public string? RemoteAddressOverride { get; set; }

    /// <summary>
    /// 1102-C: where <c>/live/stream</c> reads its frames; set by the host once its live hub exists. While it is
    /// null the route is not found.
    /// </summary>
    public LiveStreamSource? LiveSource { get; set; }

    /// <summary>Whether the listener thread is running.</summary>
    public bool IsListening => _listening;

    /// <summary>Binds the port and starts the listener thread.</summary>
    public void Start()
    {
        if (_listening)
        {
            return;
        }

        var address = IPAddress.TryParse(_options.BindAddress, out var parsed) ? parsed : IPAddress.Loopback;
        var listener = new TcpListener(address, _options.Port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _listening = true;
        _acceptThread = new Thread(Accept) { IsBackground = true, Name = "scheduler-page" };
        _acceptThread.Start();
    }

    /// <summary>Stops listening. Requests already being answered finish on their own.</summary>
    public void Stop()
    {
        _listening = false;
        _closing.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }

        _acceptThread?.Join(TimeSpan.FromSeconds(2));
        _acceptThread = null;
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        if (OwnsQueue)
        {
            Queue.Dispose();
        }
    }

    private void Accept()
    {
        var listener = _listener;
        if (listener is null)
        {
            return;
        }

        while (_listening)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return; // the listener was stopped
            }

            ThreadPool.QueueUserWorkItem(_ => Serve(client));
        }
    }

    private void Serve(TcpClient client)
    {
        var keep = false;
        try
        {
            client.ReceiveTimeout = 30_000;
            client.SendTimeout = 30_000;
            var stream = client.GetStream();
            var request = ReadRequest(stream, client);
            if (LiveSource is { } source && LiveStream.Wants(request))
            {
                // 1102-C: a stream lives as long as its page, so it gets a thread of its own and never holds a
                // pool thread or the accept thread.
                keep = true;
                var thread = new Thread(() => Stream(client, source, request)) { IsBackground = true, Name = "live-stream" };
                thread.Start();
                return;
            }

            Write(stream, Router.Handle(request));
        }
        catch (Exception)
        {
            // A browser gives up on a request that waited out a slow tick and asks again;
            // there is nobody left to tell that the answer failed.
        }
        finally
        {
            if (!keep)
            {
                client.Dispose();
            }
        }
    }

    private void Stream(TcpClient client, LiveStreamSource source, PageRequest request)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 0;
                var project = request.Query("project");
                if (string.Equals(project, "all", StringComparison.OrdinalIgnoreCase))
                {
                    project = null;
                }

                new LiveStream(source, project).Run(client.GetStream(), request.Header("Last-Event-ID"), _closing.Token);
            }
            catch (Exception)
            {
                // the page closed, or the client was too slow to take a write: the stream is over
            }
        }
    }

    private PageRequest ReadRequest(NetworkStream stream, TcpClient client)
    {
        var bytes = new List<byte>(2048);
        var one = new byte[1];
        var headerEnd = -1;
        while (bytes.Count < MaxHeaderBytes)
        {
            if (stream.Read(one, 0, 1) != 1)
            {
                break;
            }

            bytes.Add(one[0]);
            if (bytes.Count >= 4 && one[0] == '\n' && bytes[^2] == '\r' && bytes[^3] == '\n' && bytes[^4] == '\r')
            {
                headerEnd = bytes.Count;
                break;
            }
        }

        if (headerEnd < 0)
        {
            throw new IOException("the request never finished its headers");
        }

        var head = Encoding.UTF8.GetString(bytes.ToArray(), 0, headerEnd - 4);
        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2)
        {
            throw new IOException("the request has no request line");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var split = lines[index].Split(':', 2);
            if (split.Length == 2)
            {
                headers[split[0].Trim()] = split[1].Trim();
            }
        }

        var length = headers.TryGetValue("Content-Length", out var raw) && int.TryParse(raw, out var declared)
            ? Math.Min(declared, MaxBodyBytes)
            : 0;

        var body = new List<byte>(length);
        for (var index = headerEnd; index < bytes.Count && body.Count < length; index++)
        {
            body.Add(bytes[index]);
        }

        while (body.Count < length)
        {
            var read = stream.Read(one, 0, 1);
            if (read != 1)
            {
                break;
            }

            body.Add(one[0]);
        }

        var text = Encoding.UTF8.GetString(body.ToArray());
        var form = headers.TryGetValue("Content-Type", out var type)
            && type.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            ? PageRequest.ParseForm(text)
            : PageRequest.ParseQuery(string.Empty);

        var target = parts[1];
        var mark = target.IndexOf('?');
        var path = mark < 0 ? target : target[..mark];
        var query = mark < 0 ? string.Empty : target[(mark + 1)..];
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            path = new Uri(path).AbsolutePath;
        }

        var remote = RemoteAddressOverride
            ?? (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString()
            ?? "127.0.0.1";

        return new PageRequest
        {
            Method = parts[0],
            Path = Uri.UnescapeDataString(path),
            RemoteIp = remote,
            Body = text,
            QueryValues = PageRequest.ParseQuery(query),
            FormValues = form,
            Headers = headers,
        };
    }

    private static void Write(NetworkStream stream, PageResponse response)
    {
        var content = response.Content;
        var head = new StringBuilder();
        _ = head.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n");
        _ = head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        _ = head.Append("Content-Length: ").Append(content.Length).Append("\r\n");
        var cached = false;
        foreach (var (name, value) in response.Headers)
        {
            _ = head.Append(name).Append(": ").Append(value).Append("\r\n");
            cached |= string.Equals(name, "Cache-Control", StringComparison.OrdinalIgnoreCase);
        }

        if (!cached)
        {
            _ = head.Append("Cache-Control: no-store\r\n");
        }

        _ = head.Append("Connection: close\r\n\r\n");

        var headBytes = Encoding.ASCII.GetBytes(head.ToString());
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(content, 0, content.Length);
        stream.Flush();
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        202 => "Accepted",
        303 => "See Other",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Unknown",
    };
}
