// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Templates;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Templates;

/// <summary>INS-17: template refresh in a kit folder with no <c>.git</c>: templates from disk, past versions from the manifest, its version as the trigger, no git for the kit.</summary>
public sealed class TemplateRefreshNoGitTests : IDisposable
{
    private const string Readme = "assets/templates/jobs/README.md";
    private readonly GitFixture _project = new();
    private readonly TempDir _kit = new();
    private readonly ListLog _log = new();
    private readonly string _state;

    public TemplateRefreshNoGitTests()
    {
        _state = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "logs", TemplateState.FileName);
        foreach (var (k, v) in new[] { ("user.name", "scheduler"), ("user.email", "s@s"), ("commit.gpgsign", "false"), ("core.autocrlf", "false") })
        {
            GitFixture.Git(_project.Repo, "config", k, v);
        }
    }

    public void Dispose()
    {
        _project.Dispose();
        _kit.Dispose();
    }

    private void Kit(string readme, string? version, params string[] past)
    {
        var file = Path.Combine(_kit.Path, Readme.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, readme);
        if (version is not null)
        {
            var ids = string.Join(",", past.Select(t => $"\"{TemplateHistory.BlobId(t)}\""));
            File.WriteAllText(Path.Combine(_kit.Path, TemplateHistory.RelativePath), $"{{\"version\":\"{version}\",\"templates\":{{\"jobs/README.md\":[{ids}]}}}}");
        }
    }

    private TemplateRefresh Refresh(GitRunner? git = null)
    {
        GitFixture.Git(_project.Repo, "fetch", "-q", "origin");
        var answers = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "slots", "answers");
        var committer = new AnswersCommitter(new GitRunner(), _project.Repo, answers, "origin", "main");
        return new TemplateRefresh(git ?? new GitRunner(), _kit.Path, _project.Main(), committer, "jobs", _state, _log);
    }

    private string OnMain(string path) => GitFixture.Git(_project.Bare, "show", $"main:{path}");

    [Fact]
    public void INS_17_A_copy_that_is_a_past_version_is_refreshed_from_disk_with_no_kit_git()
    {
        Kit("rules v2\n", "1.0.1", "rules v1\n", "rules v2\n");
        _project.Push("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\r\n", ["jobs/project.md"] = "mine\n" });
        var git = new GitRunner();

        var refresh = Refresh(git);
        Assert.True(refresh.Step());
        Assert.Equal(0, git.Started);

        Assert.Equal("rules v2", OnMain("jobs/README.md"));
        Assert.Equal("mine", OnMain("jobs/project.md"));
        Assert.Empty(refresh.Edited);
        Assert.False(Directory.Exists(Path.Combine(_kit.Path, ".git")));
    }

    [Fact]
    public void INS_17_An_edited_copy_waits_for_Keep_or_Overwrite_as_with_git()
    {
        Kit("rules v2\n", "1.0.1", "rules v1\n", "rules v2\n");
        _project.Push("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "my own rules\n" });

        var refresh = Refresh();
        Assert.False(refresh.Step());
        Assert.Equal("jobs/README.md", Assert.Single(refresh.Edited).Target);
        Assert.Equal("my own rules", OnMain("jobs/README.md"));

        Assert.True(refresh.Overwrite("jobs/README.md").Ok);
        Assert.Equal("rules v2", OnMain("jobs/README.md"));
    }

    [Fact]
    public void INS_17_The_manifest_version_is_the_trigger_and_an_unchanged_version_checks_nothing()
    {
        Kit("rules v1\n", "1.0.0", "rules v1\n");
        _project.Push("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n" });
        var refresh = Refresh();
        Assert.False(refresh.Step());
        Assert.Equal(1, refresh.Checks);

        Assert.False(refresh.Step());
        Assert.Equal(1, refresh.Checks);

        Kit("rules v2\n", "1.0.1", "rules v1\n", "rules v2\n");
        Assert.True(refresh.Step());
        Assert.Equal(2, refresh.Checks);
        Assert.Equal("rules v2", OnMain("jobs/README.md"));
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "not json")]
    [InlineData(true, "{\"version\":\"1\"}")]
    public void INS_17_A_missing_or_unreadable_manifest_logs_once_and_keeps_nothing_in_step(bool write, string content)
    {
        Kit("rules v2\n", null);
        if (write)
        {
            File.WriteAllText(Path.Combine(_kit.Path, TemplateHistory.RelativePath), content);
        }

        _project.Push("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n" });
        var refresh = Refresh();

        Assert.False(refresh.Step());
        Assert.False(refresh.Step());
        Assert.Equal(0, refresh.Checks);
        Assert.Equal("rules v1", OnMain("jobs/README.md"));
        Assert.Single(_log.Lines, l => l.Contains("template-history.json"));
    }

    [Fact]
    public void INS_17_BlobId_is_the_id_git_gives_the_same_text()
    {
        var file = Path.Combine(_kit.Path, "x.txt");
        File.WriteAllText(file, "hello\n");
        Assert.Equal(GitFixture.Git(_kit.Path, "hash-object", "--no-filters", file).Trim(), TemplateHistory.BlobId("hello\r\n"));
    }
}
