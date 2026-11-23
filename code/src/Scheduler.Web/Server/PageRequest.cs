using System.Net;

namespace Scheduler.Web.Server;

/// <summary>
/// One request as the routes see it. The listener fills it in, which keeps the route table free
/// of sockets and lets a request be made to come from another PC (PAGE-7).
/// </summary>
public sealed record PageRequest
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>GET or POST (PAGE-9).</summary>
    public required string Method { get; init; }

    /// <summary>The path, without the query.</summary>
    public required string Path { get; init; }

    /// <summary>The address the request came from; loopback means this PC.</summary>
    public string RemoteIp { get; init; } = "127.0.0.1";

    /// <summary>The request body as text.</summary>
    public string Body { get; init; } = "";

    /// <summary>The query string, decoded.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> QueryValues { get; init; } = NoValues;

    /// <summary>The form body, decoded, when the request posted a form.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FormValues { get; init; } = NoValues;

    /// <summary>The request headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = NoHeaders;

    /// <summary>Whether the request came from the PC running the scheduler.</summary>
    public bool IsLocal =>
        IPAddress.TryParse(RemoteIp, out var address) && IPAddress.IsLoopback(address);

    /// <summary>
    /// Whether the page script made this POST rather than a plain form, which decides between the
    /// 202 with the queued id and the 303 back to the page (PAGE-1).
    /// </summary>
    public bool IsFetch =>
        string.Equals(Header("X-Requested-With"), "fetch", StringComparison.OrdinalIgnoreCase)
        || (Header("Accept") ?? string.Empty).Contains("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>One header, or null.</summary>
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>One query value, or null.</summary>
    public string? Query(string name) => First(QueryValues, name);

    /// <summary>One form value, or null.</summary>
    public string? Form(string name) => First(FormValues, name);

    /// <summary>Every value posted under a name, in order.</summary>
    public IReadOnlyList<string> FormList(string name) =>
        FormValues.TryGetValue(name, out var values) ? values : [];

    /// <summary>One query value as a number, or null.</summary>
    public int? QueryInt(string name) => Number(Query(name));

    /// <summary>One form value as a number, or null.</summary>
    public int? FormInt(string name) => Number(Form(name));

    /// <summary>Parses a <c>a=1&amp;b=2</c> body into form values.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseForm(string text) => ParsePairs(text);

    /// <summary>Parses a query string into values.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseQuery(string text) => ParsePairs(text);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParsePairs(string text)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            var name = Unescape(split[0]);
            if (name.Length == 0)
            {
                continue;
            }

            var value = Unescape(split.Length > 1 ? split[1] : "");
            values[name] = values.TryGetValue(name, out var list) ? [.. list, value] : [value];
        }

        return values;
    }

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    private static string? First(IReadOnlyDictionary<string, IReadOnlyList<string>> values, string name) =>
        values.TryGetValue(name, out var list) && list.Count > 0 ? list[0] : null;

    private static int? Number(string? text) => int.TryParse(text, out var value) ? value : null;
}
