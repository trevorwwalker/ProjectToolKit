using Scheduler.Core.Contracts;

namespace Scheduler.Core.Accounting;

/// <summary>
/// What a model server says it serves, asked at most every 10 minutes per endpoint (ACCT-3,
/// <c>Server-Models</c> L2232). A server may answer any alias with whatever it really runs, so
/// the first entry its <c>/models</c> lists is taken to be the one; the id it gives is a Hugging
/// Face id or a path, which is what <see cref="ModelKey"/> matches against models.json.
/// </summary>
public sealed class ServerModelCache(IClock clock, IServerProbe probe)
{
    private static readonly TimeSpan AskAgainAfter = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<string>? Ids)> _asked = new(Text.Comparer);

    /// <summary>The ids the server at <paramref name="endpoint"/> lists, empty when it answers nothing.</summary>
    public IReadOnlyList<string> Models(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            return [];
        }

        if (_asked.TryGetValue(endpoint, out var last) && (clock.Now - last.At) < AskAgainAfter)
        {
            return last.Ids ?? [];
        }

        // The time is kept even when the server did not answer, so a down server is not asked again every call.
        var ids = Parse(probe.ReadModels(endpoint));
        _asked[endpoint] = (clock.Now, ids);
        return ids ?? [];
    }

    /// <summary>
    /// The served name for <paramref name="model"/>: the entry whose id is that model, else the
    /// first the server lists, else "" (<c>Server-Model</c> L2242).
    /// </summary>
    public string Root(string? endpoint, string model)
    {
        var ids = Models(endpoint);
        if (ids.Count == 0)
        {
            return "";
        }

        foreach (var id in ids)
        {
            if (Text.Same(id, model))
            {
                return id;
            }
        }

        return ids[0];
    }

    private static IReadOnlyList<string>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            var ids = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object
                    && item.TryGetProperty("id", out var id)
                    && id.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    ids.Add(id.GetString()!);
                }
            }

            return ids;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
