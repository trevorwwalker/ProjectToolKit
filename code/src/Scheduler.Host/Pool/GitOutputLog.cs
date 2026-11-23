using System.Text.RegularExpressions;

namespace Scheduler.Host.Pool;

/// <summary>What kind of git trouble a worker log shows.</summary>
public enum GitTrouble
{
    /// <summary>A push git rejected.</summary>
    RejectedPush,

    /// <summary>A rebase that stopped on a conflict.</summary>
    RebaseConflict,
}

/// <summary>One rejected push or rebase conflict found in a worker log.</summary>
/// <param name="Card">The card the worker was running.</param>
/// <param name="Kind">Which it was.</param>
/// <param name="Items">The refs of a rejected push, or the files of a conflict.</param>
public sealed record GitTroubleEntry(string Card, GitTrouble Kind, IReadOnlyList<string> Items);

/// <summary>
/// SC-6 (spec B3 Q9 as answered on card 330-B): the workers do the pushing, so the scheduler reads
/// the git output in the raw worker log and logs every rejected push and every rebase conflict,
/// naming the card and what it hit.
/// </summary>
public static partial class GitOutputLog
{
    /// <summary>Find the trouble in one worker's log text and write one line for each.</summary>
    /// <param name="card">The card id.</param>
    /// <param name="logText">The raw worker log.</param>
    /// <param name="write">Where the lines go (the scheduler's own log).</param>
    public static IReadOnlyList<GitTroubleEntry> Scan(string card, string logText, Action<string> write)
    {
        var found = new List<GitTroubleEntry>();
        var files = new List<string>();

        void FlushConflict()
        {
            if (files.Count == 0)
            {
                return;
            }

            var entry = new GitTroubleEntry(card, GitTrouble.RebaseConflict, [.. files]);
            files.Clear();
            found.Add(entry);
            write($"git: card {card} rebase conflict in {string.Join(", ", entry.Items)}");
        }

        foreach (var line in logText.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (Rejected().Match(text) is { Success: true } r)
            {
                var entry = new GitTroubleEntry(card, GitTrouble.RejectedPush, [r.Groups[1].Value.Trim()]);
                found.Add(entry);
                write($"git: card {card} push rejected ({entry.Items[0]})");
            }
            else if (Conflict().Match(text) is { Success: true } c)
            {
                var file = c.Groups[1].Success ? c.Groups[1].Value : c.Groups[2].Value;
                if (!files.Contains(file))
                {
                    files.Add(file);
                }
            }
            else if (text.StartsWith("error: could not apply", StringComparison.Ordinal))
            {
                FlushConflict();
            }
        }

        FlushConflict();
        return found;
    }

    [GeneratedRegex(@"^\s*!\s*\[(?:remote )?rejected\]\s+(.+?)(?:\s+\(.*\))?\s*$")]
    private static partial Regex Rejected();

    [GeneratedRegex(@"^CONFLICT \([^)]*\):\s*(?:Merge conflict in (.+?)|(\S+) (?:deleted|modified|renamed).*)\s*$")]
    private static partial Regex Conflict();
}
