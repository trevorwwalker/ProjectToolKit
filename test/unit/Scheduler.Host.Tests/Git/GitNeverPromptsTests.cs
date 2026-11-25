// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Git;

public class GitNeverPromptsTests
{
    // CFG-16: git runs with GIT_TERMINAL_PROMPT=0 and GCM_INTERACTIVE=Never, stdin closed, and only stdout is parsed.
    [Fact]
    public void Config_GitNeverPrompts()
    {
        using var temp = new TempDir();
        var fake = Path.Combine(temp.Path, "fakegit.cmd");
        var record = Path.Combine(temp.Path, "record.txt");
        var stdin = Path.Combine(temp.Path, "stdin.txt");
        File.WriteAllText(fake, string.Join("\r\n",
            "@echo off",
            $">>\"{record}\" echo GIT_TERMINAL_PROMPT=%GIT_TERMINAL_PROMPT%",
            $">>\"{record}\" echo GCM_INTERACTIVE=%GCM_INTERACTIVE%",
            $"more > \"{stdin}\"",
            "echo 100644 blob abc123\tdocs/jobs/queue/010-A-x.md",
            "echo warning: noise 1>&2",
            "exit /b 0",
            ""));

        var repo = new MainRepository(new GitRunner(fake, TimeSpan.FromSeconds(30)), temp.Path, "origin", "main");
        var tree = repo.ListTree("docs/jobs");

        var entry = Assert.Single(tree!);
        Assert.Equal("docs/jobs/queue/010-A-x.md", entry.Path);
        Assert.Equal("abc123", entry.BlobId);
        var env = File.ReadAllText(record);
        Assert.Contains("GIT_TERMINAL_PROMPT=0", env);
        Assert.Contains("GCM_INTERACTIVE=Never", env);
        Assert.Equal("", File.ReadAllText(stdin).Trim());
    }
}
