using Scheduler.Core.Tests.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// 1063-B (state-cache.md MM-9, MM-14): MP-1's per-project read comes from each repository's own model of main once
/// it is loaded - its gate's fetch, no git process - and through git as before for a repository without one.
/// </summary>
public sealed class PoolModelTests
{
    [Fact]
    public void Pool_ProjectsReadFromTheirOwnModel_NoGit_ProjectWithoutModelReadThroughGit()
    {
        var p = new FakeFeed(new FakeMain(new Dictionary<string, string>
        {
            ["docs/jobs/scheduler.json"] = "{ \"who\": \"P\" }",
            ["docs/jobs/README.md"] = "# P readme",
            ["docs/jobs/categories.json"] = "{ \"lanes\": \"P\" }",
            ["docs/jobs/queue/010-A-p.md"] = "# 010-A: p\n\nStatus: ready\n",
            ["docs/jobs/done/009-A-q.md"] = "# 009-A: q\n\nStatus: done\n",
            ["docs/jobs/queue/notes.txt"] = "not a card",
        }, ["docs/jobs"]));
        var json = """
            { "projects": [
                { "name": "P", "repo": "C:/pool/p", "jobsDir": "docs/jobs", "weight": 70 },
                { "name": "O", "repo": "C:/pool/o", "jobsDir": "jobs", "weight": 30 } ] }
            """;
        var git = new Dictionary<string, CountingMain>();

        var jobs = new PoolProjectReader(
            (repo, _, _) => git[repo] = new CountingMain(),
            (repo, _, _) => repo.EndsWith("/p", StringComparison.Ordinal) ? p : null).Read(PoolConfig.Parse(json));

        Assert.Equal("{ \"who\": \"P\" }", jobs[0].SchedulerJson);
        Assert.Equal("# P readme", jobs[0].Readme);
        Assert.Equal("{ \"lanes\": \"P\" }", jobs[0].CategoriesJson);
        Assert.Equal(["docs/jobs/queue/010-A-p.md", "docs/jobs/done/009-A-q.md"], jobs[0].Cards);
        Assert.Equal(1, p.Fetches);
        Assert.Equal(["C:/pool/o"], git.Keys);   // only the project without a model started git
        Assert.Contains("fetch", git["C:/pool/o"].Calls);
    }
}
