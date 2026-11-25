// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;

namespace Scheduler.Core.Locks;

/// <summary>
/// Lock request files (design <c>dynamic-locks.md</c>, section 3): <c>&lt;yyyyMMdd-HHmmss-fff&gt;-slot&lt;N&gt;.lock</c>
/// in the moves folder beside the move requests (<see cref="CardRequests"/>), header lines
/// <c>Card:</c>, <c>Slot:</c>, <c>Paths:</c> (one per line after it) and <c>Why:</c>; the reply is a file
/// of the same name ending <c>.reply</c> in <c>moves\replies\</c>. Pure; Host reads the folder.
/// </summary>
public static class LockRequests
{
    /// <summary>The extension of a lock request ready to be read; a writer renames its <c>.tmp</c> to it.</summary>
    public const string Extension = ".lock";

    /// <summary>The folder under the moves folder that holds the replies.</summary>
    public const string RepliesFolder = "replies";

    /// <summary>The extension of a reply.</summary>
    public const string ReplyExtension = ".reply";

    private static readonly string[] Keys = ["Card", "Slot", "Paths", "Why"];

    /// <summary>A request's file name: the time, so names sort in the order they were written, then the slot.</summary>
    public static string FileName(DateTimeOffset now, int slot) =>
        now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + $"-slot{slot}{Extension}";

    /// <summary>The reply's file name for a request file name.</summary>
    public static string ReplyName(string requestFileName) => Path.ChangeExtension(requestFileName, ReplyExtension);

    /// <summary>The order requests are answered in: by file name, ordinal (time first, then slot).</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> fileNames) =>
        [.. fileNames.Where(n => n.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];

    /// <summary>The text of a request, as the <c>lock</c> command writes it.</summary>
    public static string Text(string card, int slot, IEnumerable<string> paths, string why) =>
        $"Card: {card}\nSlot: {slot}\nPaths:\n{string.Concat(paths.Select(p => $"  {p}\n"))}Why: {why}\n";

    /// <summary>
    /// Reads a request. <c>Card</c> and at least one path are required. Paths may follow
    /// <c>Paths:</c> on its line and on the lines after it (a leading <c>- </c> is dropped,
    /// backslashes become slashes), up to the next header.
    /// </summary>
    public static LockRequest Parse(string fileName, string content)
    {
        content = (content ?? "").TrimStart('﻿');
        var header = new Dictionary<string, string>(Scheduler.Core.Contracts.Text.Comparer);
        var paths = new List<string>();
        var inPaths = false;
        foreach (var raw in CardText.Split(content))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var m = Scheduler.Core.Contracts.Text.Match(line, @"^([A-Za-z]+)\s*:\s*(.*)$");
            if (m.Success && Keys.Contains(m.Groups[1].Value, Scheduler.Core.Contracts.Text.Comparer))
            {
                var key = m.Groups[1].Value;
                inPaths = Scheduler.Core.Contracts.Text.Same(key, "Paths");
                if (inPaths)
                {
                    AddPath(paths, m.Groups[2].Value);
                }
                else
                {
                    header[key] = m.Groups[2].Value.Trim();
                }
            }
            else if (inPaths)
            {
                AddPath(paths, line);
            }
            else
            {
                return LockRequest.Failed(fileName, $"not a header line: {line}");
            }
        }

        var card = header.GetValueOrDefault("Card", "");
        if (card.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            card = card[..^3];
        }

        if (card.Length == 0)
        {
            return LockRequest.Failed(fileName, "no Card: line");
        }

        if (paths.Count == 0)
        {
            return LockRequest.Failed(fileName, $"{card}: no paths");
        }

        var slotText = header.GetValueOrDefault("Slot", "").Replace("slot", "", StringComparison.OrdinalIgnoreCase);
        int? slot = int.TryParse(slotText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : null;
        return new LockRequest
        {
            FileName = fileName,
            Card = card,
            Slot = slot,
            Paths = [.. paths.Distinct(StringComparer.Ordinal)],
            Why = header.GetValueOrDefault("Why", ""),
        };
    }

    /// <summary>
    /// DL-22: the card in a request is checked against the card the slot that wrote it is running,
    /// never taken on the worker's word. <paramref name="slotCard"/> is that card, or null when the
    /// slot runs none.
    /// </summary>
    public static LockRequest Verify(LockRequest request, string? slotCard)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Ok)
        {
            return request;
        }

        if (slotCard is null)
        {
            return LockRequest.Failed(request.FileName, $"unknown card: slot {request.Slot} runs no card");
        }

        return Scheduler.Core.Contracts.Text.Same(slotCard, request.Card)
            ? request
            : LockRequest.Failed(request.FileName, $"unknown card: slot {request.Slot} runs {slotCard}, not {request.Card}");
    }

    private static void AddPath(List<string> paths, string text)
    {
        var p = text.Trim();
        if (p.StartsWith("- ", StringComparison.Ordinal))
        {
            p = p[2..].Trim();
        }

        if (p.Length > 0)
        {
            paths.Add(p.Replace('\\', '/'));
        }
    }
}
