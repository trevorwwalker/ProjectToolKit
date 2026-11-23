using System.Net.Http;
using System.Text;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// The Web project running in-process on a free port, with the contract faked behind it
/// (test-plans/index.md: HTTP tests run the Web project in-process on a free port).
/// </summary>
internal sealed class WebHarness : IDisposable
{
    private readonly TempFolder _logs = new();

    public WebHarness(bool remoteControl = true, string? remoteAddress = null)
    {
        Data = new FakePageData { LogDirectory = _logs.Path };
        Actions = new FakePageActions();
        Renderer = new FakePageRenderer();
        Clock = new TestClock();
        Queue = new PageActionQueue();
        Feed = new LiveFeed(Clock, Data) { Actions = Queue };
        Server = new PageServer(new PageServerOptions
        {
            Port = 0,
            RemoteControl = remoteControl,
            Data = Data,
            Actions = Actions,
            Renderer = Renderer,
            Feed = Feed,
            Queue = Queue,
        });
        Server.Start();
        if (remoteAddress is not null)
        {
            Server.RemoteAddressOverride = remoteAddress;
        }

        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Server.Port}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public FakePageData Data { get; }

    public FakePageActions Actions { get; }

    public FakePageRenderer Renderer { get; }

    public TestClock Clock { get; }

    public LiveFeed Feed { get; }

    /// <summary>The one worker the POST actions run on (957-B).</summary>
    public PageActionQueue Queue { get; }

    public PageServer Server { get; }

    public HttpClient Client { get; }

    /// <summary>The log folder the /log routes read.</summary>
    public string LogDir => _logs.Path;

    /// <summary>Puts a log file in the log folder and returns its path.</summary>
    public string WriteLog(string name, string text) => _logs.Write(name, text);

    /// <summary>
    /// A POST the page script makes: answered 202 with the queued id (PAGE-1). The work itself runs
    /// on the queue's worker thread, so a test that wants to read what the click did says so with
    /// <paramref name="drain"/>, which waits for the queue to empty after the reply (957-B). A test
    /// that is checking that the reply does NOT wait for the work passes false.
    /// </summary>
    public async Task<HttpResponseMessage> PostFetchAsync(string path, string? body = null, bool drain = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body ?? "", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.TryAddWithoutValidation("X-Requested-With", "fetch");
        var response = await Client.SendAsync(request);
        if (drain)
        {
            WaitActions();
        }

        return response;
    }

    /// <summary>A POST from a plain form: answered 303 back to the page (PAGE-1).</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string path, string? body = null, bool drain = true)
    {
        var response = await Client.PostAsync(path, new StringContent(body ?? "", Encoding.UTF8, "application/x-www-form-urlencoded"));
        if (drain)
        {
            WaitActions();
        }

        return response;
    }

    /// <summary>A POST with a JSON Accept header, the other way the page script asks (PAGE-1).</summary>
    public async Task<HttpResponseMessage> PostJsonAcceptAsync(string path, bool drain = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        var response = await Client.SendAsync(request);
        if (drain)
        {
            WaitActions();
        }

        return response;
    }

    /// <summary>Waits for the queue's worker thread to finish every click posted so far.</summary>
    public void WaitActions() => Assert.True(Queue.WaitIdle(TimeSpan.FromSeconds(10)), "the page's action queue never went idle");

    public void Dispose()
    {
        Client.Dispose();
        Server.Dispose();
        Queue.Dispose();
        _logs.Dispose();
    }
}
