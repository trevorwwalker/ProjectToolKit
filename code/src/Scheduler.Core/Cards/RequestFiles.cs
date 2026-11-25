// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// The requests to the planner (CARD-18): a faithful port of <c>Read-Requests</c>
/// (schedule.ps1 L649-666) plus the page's filter for the open ones (L3604). They stay in
/// <c>requests/</c> once answered, as the record, so each one carries whether it is still open.
/// Reading git is Host's (<see cref="IMainRepository.ReadFile"/>); this decides which paths are
/// requests and what their text means.
/// </summary>
public static class RequestFiles
{
    /// <summary>
    /// The requests among <paramref name="paths"/>, in name order: a file directly under
    /// <c>{jobsDir}/requests/</c> whose name ends in <c>.md</c> and does not contain README, with
    /// its first Title, Status and For lines from <paramref name="readText"/>. Text that cannot be
    /// read counts as empty, so the request is still listed.
    /// </summary>
    public static IReadOnlyList<JobRequest> Read(
        string jobsDir,
        IEnumerable<string> paths,
        Func<string, string?> readText)
    {
        var pattern = "^" + Regex.Escape(jobsDir + "/requests/") + @"[^/]+\.md$";
        var requests = new List<JobRequest>();

        foreach (var path in paths
                     .Where(path => Text.IsMatch(path, pattern) && !Text.IsMatch(path, "README"))
                     .OrderBy(path => path, Text.SortComparer))
        {
            var title = "";
            var status = "";
            var forCard = "";

            foreach (var line in (readText(path) ?? "").Split('\n'))
            {
                // L656-660: the first of each of the three lines wins, and a line is only asked to
                // be what the card still lacks.
                if (title == "" && Text.Match(line, @"^#\s+(.*)$") is { Success: true } t)
                {
                    title = t.Groups[1].Value.Trim();
                }
                else if (status == "" && Text.Match(line, @"^Status:\s*(.*)$") is { Success: true } s)
                {
                    status = s.Groups[1].Value.Trim();
                }
                else if (forCard == "" && Text.Match(line, @"^For:\s*(.*)$") is { Success: true } f)
                {
                    forCard = f.Groups[1].Value.Trim();
                }
            }

            requests.Add(new JobRequest
            {
                Path = path,
                Name = NameOf(path),
                Title = title,
                Status = status,
                For = forCard,
                Open = IsOpen(status),
            });
        }

        return requests;
    }

    /// <summary>
    /// Whether the page lists a request among the open ones (L3604): its Status does not start
    /// <c>answered</c>, <c>closed</c> or <c>done</c>. No Status at all is open.
    /// </summary>
    public static bool IsOpen(string status) => !Text.IsMatch(status, @"^(answered|closed|done)");

    // The file name without the extension, as Card.Name is the card's file name without it.
    private static string NameOf(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
