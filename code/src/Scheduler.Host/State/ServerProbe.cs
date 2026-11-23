using Scheduler.Core.Contracts;

namespace Scheduler.Host.State;

/// <summary>
/// The model server over HTTP. Host makes the request and gives up on the script's timeouts:
/// 5 s at <c>/models</c> for the up check (ACCT-8, L1005), 3 s for the model list (ACCT-3, L2238)
/// and 2 s for the metrics (ACCT-7, L1034). The 20 s cache and the logging of the changes are
/// Core's ServerCache.
/// </summary>
public sealed class ServerProbe : IServerProbe, IDisposable
{
    private static readonly TimeSpan UpTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ModelsTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MetricsTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http = new();

    /// <summary>GET <c>&lt;endpoint&gt;/models</c>: does the server answer at all (ACCT-8).</summary>
    public bool IsUp(string endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) || Get(endpoint.TrimEnd('/') + "/models", UpTimeout) is not null;

    /// <summary>The <c>/models</c> JSON, or null when the server did not answer (ACCT-3).</summary>
    public string? ReadModels(string endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ? null : Get(endpoint.TrimEnd('/') + "/models", ModelsTimeout);

    /// <summary>The Prometheus text at the metrics URL, or null (ACCT-7).</summary>
    public string? ReadMetrics(string metricsUrl) =>
        string.IsNullOrWhiteSpace(metricsUrl) ? null : Get(metricsUrl, MetricsTimeout);

    /// <summary>One GET, waited on with its own timeout; anything short of a body counts as no answer.</summary>
    private string? Get(string url, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using var response = _http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null; // down, or too slow: the caller decides what that means
        }
    }

    /// <summary>Release the connections.</summary>
    public void Dispose() => _http.Dispose();
}
