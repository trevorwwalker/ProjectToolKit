// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Scopes;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Config;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// Host's part of the pool and the scopes (card 330-B): the top-level config and what is read per
/// project (MP-1, Q12), rejected pushes and rebase conflicts in the raw worker log (SC-6, Q9), and
/// a pushed change outside a card's scope (SC-7, Q8).
/// </summary>
public sealed class PoolTests
{
    // MP-1, Q12: the top-level config lists P and O, each with its own repository and jobs folder, and
    // holds the page's port, theme and remote control. Cards, categories, README and scheduler.json are read
    // from each project's own main.
    [Fact]
    public void Pool_TopLevelConfig_ProjectsKeepOwnRepoAndJobs()
    {
        using var fx = new ConfigFixture();
        var p = Project(fx, "p", "docs/jobs", "P");
        var o = Project(fx, "o", "jobs", "O");
        var json = $$"""
            { "port": 9000, "theme": "dark", "remoteControl": true,
              "projects": [
                { "name": "P", "repo": "{{Slash(p)}}", "jobsDir": "docs/jobs", "weight": 70 },
                { "name": "O", "repo": "{{Slash(o)}}", "jobsDir": "jobs", "weight": 30 } ] }
            """;

        var config = PoolConfig.Parse(json);
        var jobs = new PoolProjectReader(fx.RemoteRepo).Read(config);

        Assert.Equal(9000, config.Port);
        Assert.Equal("dark", config.Theme);
        Assert.True(config.RemoteControl);
        Assert.Equal(["P", "O"], jobs.Select(j => j.Name));
        Assert.Equal([70, 30], jobs.Select(j => j.Weight));
        Assert.Equal("{ \"who\": \"P\" }", jobs[0].SchedulerJson);
        Assert.Equal("{ \"who\": \"O\" }", jobs[1].SchedulerJson);
        Assert.Equal("# P readme", jobs[0].Readme);
        Assert.Equal("# O readme", jobs[1].Readme);
        Assert.Equal("{ \"lanes\": \"P\" }", jobs[0].CategoriesJson);
        Assert.Equal("{ \"lanes\": \"O\" }", jobs[1].CategoriesJson);
        Assert.Equal(["docs/jobs/queue/010-A-p.md"], jobs[0].Cards);
        Assert.Equal(["jobs/queue/010-A-o.md"], jobs[1].Cards);
    }

    private static string Slash(string path) => path.Replace((char)92, (char)47);

    private static string Project(ConfigFixture fx, string name, string jobsDir, string who)
    {
        var bare = fx.InitBare(name + ".git");
        var repo = fx.InitRepo(name);
        fx.AddRemote(repo, "origin", bare);
        fx.Write(repo, jobsDir + "/scheduler.json", "{ \"who\": \"" + who + "\" }");
        fx.Write(repo, jobsDir + "/README.md", "# " + who + " readme");
        fx.Write(repo, jobsDir + "/lanes.json", "{ \"lanes\": \"" + who + "\" }");
        fx.Write(repo, jobsDir + "/queue/010-A-" + name + ".md", "# card");
        fx.Commit(repo, "jobs");
        fx.Push(repo, "origin", "main");
        return repo;
    }

    // SC-6, Q9: the scheduler reads the git output in the raw worker log: one entry for the rejected
    // push and one for the rebase conflict, each naming the card (and, for the conflict, the files).
    [Fact]
    public void Scope_RejectedPushAndConflict_Logged()
    {
        const string log = """
            To https://git.local/twalker/ProjectToolkit
             ! [rejected]        HEAD -> main (fetch first)
            error: failed to push some refs to 'https://git.local/twalker/ProjectToolkit'
            Auto-merging src/a.cs
            CONFLICT (content): Merge conflict in src/a.cs
            CONFLICT (add/add): Merge conflict in src/b.cs
            error: could not apply 1a2b3c4... jobs: journal
            """;
        var lines = new List<string>();

        var found = GitOutputLog.Scan("330-B", log, new Action<string>(lines.Add));

        Assert.Equal(2, found.Count);
        Assert.Equal(GitTrouble.RejectedPush, found[0].Kind);
        Assert.Equal(["HEAD -> main"], found[0].Items);
        Assert.Equal(GitTrouble.RebaseConflict, found[1].Kind);
        Assert.Equal(["src/a.cs", "src/b.cs"], found[1].Items);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Contains("330-B", l));
        Assert.Contains("HEAD -> main", lines[0]);
        Assert.Contains("src/a.cs", lines[1]);
        Assert.Contains("src/b.cs", lines[1]);
    }

    // SC-7, Q8: a card scoped to src/a.cs pushes src/b.cs as well: the pushed change is checked and the
    // card is logged as out of scope (nothing else happens to it).
    [Fact]
    public void Scope_OutOfScopeChange_Detected()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "src/a.cs", "a0");
        fx.Write(repo, "src/b.cs", "b0");
        fx.Commit(repo, "base");
        var baseSha = GitFixture.Git(repo, "rev-parse", "HEAD").Trim();
        fx.Write(repo, "src/a.cs", "a1");
        fx.Write(repo, "src/b.cs", "b1");
        fx.Commit(repo, "the card's push");
        var lines = new List<string>();

        var outside = new ScopeAudit(fx.Git).Check(repo, "330-B", Scope.Of("src/a.cs"), baseSha, "HEAD", lines.Add);

        Assert.Equal(["src/b.cs"], outside);
        var line = Assert.Single(lines);
        Assert.Contains("330-B", line);
        Assert.Contains("out of scope", line);
        Assert.Contains("src/b.cs", line);
        Assert.DoesNotContain("src/a.cs", line);
    }
}
