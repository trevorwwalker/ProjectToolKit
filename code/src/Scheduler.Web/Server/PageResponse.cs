using System.Text;

namespace Scheduler.Web.Server;

/// <summary>What a route answers: the status, the content type, the text and any extra header.</summary>
public sealed record PageResponse
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The HTTP status.</summary>
    public required int Status { get; init; }

    /// <summary>The content type, with its charset.</summary>
    public string ContentType { get; init; } = "text/plain; charset=utf-8";

    /// <summary>The body as text, when it is text.</summary>
    public string Body { get; init; } = "";

    /// <summary>The body as bytes, when it is a file; it wins over <see cref="Body"/>.</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>Extra headers, for example <c>Location</c>.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = NoHeaders;

    /// <summary>The bytes to write.</summary>
    public byte[] Content => Bytes ?? Encoding.UTF8.GetBytes(Body);

    /// <summary>A text answer, as the plain-text routes give it.</summary>
    public static PageResponse Text(int status, string text) =>
        new() { Status = status, Body = text };

    /// <summary>The page itself.</summary>
    public static PageResponse Html(string html) =>
        new() { Status = 200, ContentType = "text/html; charset=utf-8", Body = html };

    /// <summary>A JSON answer (/live.json, /status.json, and the id of a queued click).</summary>
    public static PageResponse Json(string json) =>
        new() { Status = 200, ContentType = "application/json; charset=utf-8", Body = json };

    /// <summary>202 with the id of the queued action (PAGE-1).</summary>
    public static PageResponse Accepted(string json) =>
        new() { Status = 202, ContentType = "application/json; charset=utf-8", Body = json };

    /// <summary>303 back to the page, the answer to a form POST (PAGE-1).</summary>
    public static PageResponse SeeOther(string location) =>
        new()
        {
            Status = 303,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Location"] = location },
        };

    /// <summary>403, for a control from another PC (PAGE-7).</summary>
    public static PageResponse Forbidden(string text) => new() { Status = 403, Body = text };

    /// <summary>404.</summary>
    public static PageResponse NotFound(string text) => new() { Status = 404, Body = text };

    /// <summary>405, for a method that is neither GET nor POST.</summary>
    public static PageResponse MethodNotAllowed() => new() { Status = 405, Body = "method not allowed" };

    /// <summary>A file with a long cache life, as the page font gets.</summary>
    public static PageResponse File(byte[] bytes, string contentType) =>
        new()
        {
            Status = 200,
            ContentType = contentType,
            Bytes = bytes,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Cache-Control"] = "max-age=86400" },
        };
}
