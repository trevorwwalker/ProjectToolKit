// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The commits of one card for the review panel (roadmap item 6, card 650-C): those on a branch
/// whose message names the card id, from the card's last <c>jobs: start</c> commit on, with the
/// files each changed and their line counts, read from git.
/// </summary>
public sealed class ReviewCommits(GitRunner git, string repoDir, string rev)
{
    private const char Unit = '\u001f';

    /// <summary>The commits naming <paramref name="cardId"/> (e.g. <c>650-C</c>) since its claim, oldest first.</summary>
    public IReadOnlyList<ReviewCommit> For(string cardId)
    {
        if (cardId.Length == 0)
        {
            return [];
        }

        var log = git.Run(repoDir, "log", rev, "--fixed-strings", "--grep=" + cardId, "--numstat", "--format=%x1e%H%x1f%cI%x1f%s");
        if (!log.Ok)
        {
            return [];
        }

        var commits = new List<ReviewCommit>();
        foreach (var block in log.Stdout.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var head = lines[0].Split(Unit);
            if (head.Length < 3 || !DateTimeOffset.TryParse(head[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            commits.Add(new ReviewCommit { Sha = head[0], Time = time, Subject = head[2], Files = lines.Skip(1).Select(File).OfType<ReviewFile>().ToList() });
        }

        commits.Reverse();
        var claim = commits.FindLastIndex(c => c.Subject.StartsWith("jobs: start", StringComparison.Ordinal));
        return claim > 0 ? commits.Skip(claim).ToList() : commits;
    }

    private static ReviewFile? File(string line)
    {
        var parts = line.Split('\t', 3);
        if (parts.Length < 3)
        {
            return null;
        }

        // A binary file counts "-" for both.
        _ = int.TryParse(parts[0], CultureInfo.InvariantCulture, out var added);
        _ = int.TryParse(parts[1], CultureInfo.InvariantCulture, out var removed);
        return new ReviewFile(parts[2], added, removed);
    }
}
