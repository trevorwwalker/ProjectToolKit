using Scheduler.Host.Git;
using Scheduler.Host.Templates;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// 1065-B, state-cache.md MM-14: the template check reads ProjectToolkit's repository in process - its main's
/// commit, a template's text (decoded as git's output is, a byte order mark taken off) and every past version
/// of each template from a walk of its history - the same as <c>rev-parse</c>, <c>git show</c> and
/// <c>git log --raw</c>, with no git started; a later check walks only the new commits.
/// </summary>
public sealed class TemplateRefreshInProcessTests : IDisposable
{
    private const string Readme = "assets/templates/jobs/README.md";
    private const string Card = "assets/templates/jobs/card-template.md";
    private readonly GitFixture _toolkit = new();
    private readonly GitFixture _project = new();
    private readonly TempDir _temp = new();

    public void Dispose()
    {
        _toolkit.Dispose();
        _project.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void MM_14_TemplateReads_InProcessMatchGit_AndStartNoGit()
    {
        _toolkit.Push("v1", new Dictionary<string, string?> { [Readme] = "rules v1\n", ["other.txt"] = "x\n" });
        _toolkit.Push("v2", new Dictionary<string, string?> { [Readme] = "﻿rules v2 ü\r\n" });
        _toolkit.Push("card", new Dictionary<string, string?> { [Card] = "card v1\n" });
        _toolkit.Push("unrelated", new Dictionary<string, string?> { ["other.txt"] = "y\n" });
        GitFixture.Git(_toolkit.Repo, "fetch", "-q", "origin");

        var git = new GitRunner();
        var refresh = Refresh(git);
        Same(refresh, git);

        _toolkit.Push("v3", new Dictionary<string, string?> { [Readme] = "rules v3\n", [Card] = null });
        GitFixture.Git(_toolkit.Repo, "fetch", "-q", "origin");
        Same(refresh, git);
    }

    private void Same(TemplateRefresh refresh, GitRunner git)
    {
        var main = new MainRepository(new GitRunner(), _toolkit.Repo, "origin", "main");
        var started = git.Started;
        Assert.Equal(main.HeadSha(), refresh.ToolkitHead());
        foreach (var path in new[] { Readme, Card, "assets/templates/missing.md" })
        {
            Assert.Equal(main.ReadFile(path), refresh.ToolkitFile(path));
        }

        var mine = refresh.Files.ToDictionary(f => f.Template, f => refresh.PastVersionsInProcess(f.Template));
        Assert.Equal(started, git.Started);
        foreach (var f in refresh.Files)
        {
            Assert.NotNull(mine[f.Template]);
            Assert.Equal(refresh.PastVersionsByGit(f.Template).Order(StringComparer.Ordinal), mine[f.Template]!.Order(StringComparer.Ordinal));
        }
    }

    private TemplateRefresh Refresh(GitRunner git) =>
        new(git, _toolkit.Repo, _project.Main(), new SeedCommitter(_project), "jobs", Path.Combine(_temp.Path, "templates.json"));
}
