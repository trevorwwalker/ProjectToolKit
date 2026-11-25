// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// The requests to the planner, one file each under <c>requests/</c> (CARD-18,
/// <c>Read-Requests</c> L649-666 and the page's open filter at L3604): README is not a request, the
/// first Title/Status/For line wins, and a request is open unless its Status starts answered,
/// closed or done. Test plan: scheduler-next/test-plans/cards.md.
/// </summary>
public class RequestFilesTests
{
    private const string Jobs = "docs/jobs";

    private static IReadOnlyList<JobRequest> Read(params (string Path, string? Text)[] files)
    {
        var byPath = new Dictionary<string, string?>(Text.Comparer);
        foreach (var (path, text) in files)
        {
            byPath[path] = text;
        }

        return RequestFiles.Read(Jobs, byPath.Keys, path => byPath[path]);
    }

    // CARD-18.
    [Fact]
    public void Cards_Requests_ReadAndOpenFlag()
    {
        var requests = Read(
            ($"{Jobs}/requests/README.md", "# Requests\nStatus: open\n"),
            ($"{Jobs}/requests/001-x.md", "# T\nStatus: open\nFor: 010-A\n"),
            ($"{Jobs}/requests/002-y.md", "# U\nStatus: Answered\n"),
            ($"{Jobs}/requests/003.md", "# V\n"));

        // README is skipped; the three files come back in name order (L651-652).
        Assert.Equal(["001-x", "002-y", "003"], requests.Select(r => r.Name).ToArray());
        Assert.Equal($"{Jobs}/requests/001-x.md", requests[0].Path);

        // Open: 001 and 003 (no Status at all is not answered), closed: 002. The word is matched
        // without regard to case (L3604, -notmatch).
        Assert.Equal([true, false, true], requests.Select(r => r.Open).ToArray());

        // Title, Status and For are the first of each line (L656-660).
        Assert.Equal("T", requests[0].Title);
        Assert.Equal("open", requests[0].Status);
        Assert.Equal("010-A", requests[0].For);
        Assert.Equal("U", requests[1].Title);
        Assert.Equal("", requests[2].Status);
        Assert.Equal("", requests[2].For);

        var firstWins = Read(($"{Jobs}/requests/004-z.md", "# A\n# B\nStatus: one\nStatus: two\nFor: x\nFor: y"));
        Assert.Equal("A", Assert.Single(firstWins).Title);
        Assert.Equal("one", Assert.Single(firstWins).Status);
        Assert.Equal("x", Assert.Single(firstWins).For);

        // Only a file directly under requests/ whose name ends in .md is a request, and a name with
        // README anywhere in it is skipped whatever its case (L651).
        Assert.Empty(Read(
            ($"{Jobs}/requests/sub/005-q.md", "Status: open"),
            ($"{Jobs}/requests/notes.txt", "Status: open"),
            ($"{Jobs}/queue/006-A-r.md", "Status: open"),
            ($"{Jobs}/requests/readme.md", "Status: open"),
            ($"{Jobs}/requests/README-notes.md", "Status: open")));

        // A file git could not show is still listed, with no fields (L655 on $null).
        var unreadable = Assert.Single(Read(($"{Jobs}/requests/007-s.md", null)));
        Assert.Equal("007-s", unreadable.Name);
        Assert.Equal("", unreadable.Title);
        Assert.True(unreadable.Open);
    }
}
