// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.PageModel;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The stats line of a worker's window (PAGE-10, 460-B), a port of schedule.ps1's <c>Stats-Text</c>:
/// context, compactions, tokens a second, time to the first token, model calls and tools, then the
/// model and its server. The model's context size is not known here, so the context has no share.
/// </summary>
public static class WorkerStatsText
{
    /// <summary>The line; with no stats yet, just the model and server.</summary>
    public static string Line(WorkerStats? s, string model, string endpoint)
    {
        var server = string.Join("  |  ", new[] { model, endpoint }.Where(part => part.Length > 0));
        if (s is null)
        {
            return server;
        }

        var parts = new List<string> { "context ~" + Kilo(s.CtxChars / 3.5) };
        if (s.Compactions > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"compacted {s.Compactions}x"));
        }

        if (s.GenSeconds > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"~{s.LastTps} T/s now, {Math.Round(s.GenTokens / s.GenSeconds, 1)} avg"));
        }

        if (s.LastTtft != 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"first token {s.LastTtft} s"));
        }

        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{s.Calls} model calls"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{s.Tools} tools{(s.ToolFails > 0 ? $", {s.ToolFails} failed" : "")}"));
        if (server.Length > 0)
        {
            parts.Add(server);
        }

        return string.Join("  |  ", parts);
    }

    /// <summary>A count in k or M (<c>Kilo</c>).</summary>
    public static string Kilo(double n) =>
        n >= 1_000_000 ? (n / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M"
        : n >= 1000 ? (n / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : n.ToString("0", CultureInfo.InvariantCulture);
}
