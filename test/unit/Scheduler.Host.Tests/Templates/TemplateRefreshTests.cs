// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Templates;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// The generic files kept in step with the toolkit's templates (roadmap item 1, card 540-B): a fake
/// toolkit (a temporary repository with <c>assets/templates/</c> and a remote) and a temporary
/// project committed to through its answers worktree, as the scheduler does.
/// </summary>
[ParallelMethods]
public sealed class TemplateRefreshTests : IDisposable
{
    private const string Readme = "assets/templates/jobs/README.md";

    private readonly GitFixture _toolkit = new();
    private readonly GitFixture _project = new();
    private readonly ListLog _log = new();
    private readonly string _state;

    public TemplateRefreshTests()
    {
        _state = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "logs", TemplateState.FileName);
        foreach (var repo in new[] { _project.Repo, _toolkit.Repo })
        {
            GitFixture.Git(repo, "config", "user.name", "scheduler");
            GitFixture.Git(repo, "config", "user.email", "s@s");
            GitFixture.Git(repo, "config", "commit.gpgsign", "false");
            GitFixture.Git(repo, "config", "core.autocrlf", "false");
        }
    }

    public void Dispose()
    {
        _toolkit.Dispose();
        _project.Dispose();
    }

    private TemplateRefresh Refresh(string jobsDir = "jobs")
    {
        GitFixture.Git(_project.Repo, "fetch", "-q", "origin");
        var answers = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "slots", "answers");
        var committer = new AnswersCommitter(new GitRunner(), _project.Repo, answers, "origin", "main");
        return new TemplateRefresh(new GitRunner(), _toolkit.Repo, _project.Main(), committer, jobsDir, _state, _log);
    }

    private void Toolkit(string message, string readme) => _toolkit.Push(message, new Dictionary<string, string?> { [Readme] = readme });

    private void Project(string message, IDictionary<string, string?> files) => _project.Push(message, files);

    private string OnMain(string path) => GitFixture.Git(_project.Bare, "show", $"main:{path}");

    private string LastMessage() => GitFixture.Git(_project.Bare, "log", "-1", "--format=%s", "main");

    private int Commits() => int.Parse(GitFixture.Git(_project.Bare, "rev-list", "--count", "main"));

    [Fact]
    public void A_copy_that_differs_and_is_a_past_template_version_is_refreshed_in_one_commit()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        _toolkit.Push("card", new Dictionary<string, string?> { ["assets/templates/jobs/card-template.md"] = "card v1\n" });
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n", ["jobs/project.md"] = "mine\n" });
        var before = Commits();

        Assert.True(Refresh().Step());

        Assert.Equal("rules v2", OnMain("jobs/README.md"));
        Assert.Equal("card v1", OnMain("jobs/card-template.md"));
        Assert.Equal("mine", OnMain("jobs/project.md"));
        Assert.Equal(before + 1, Commits());
        var sha = GitFixture.Git(_toolkit.Bare, "rev-parse", "--short=7", "main");
        Assert.Equal($"jobs: refresh the generic files from the toolkit ({sha})", LastMessage());
    }

    [Fact]
    public void No_commit_when_every_copy_equals_its_template_even_with_CRLF_line_ends()
    {
        Toolkit("v1", "rules v1\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\r\n" });
        var before = Commits();

        var refresh = Refresh();
        Assert.False(refresh.Step());

        Assert.Equal(before, Commits());
        Assert.Empty(refresh.Edited);
    }

    [Fact]
    public void An_edited_copy_is_listed_and_not_overwritten()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\nmy own rule\n" });
        var before = Commits();

        var refresh = Refresh();
        Assert.False(refresh.Step());

        Assert.Equal(before, Commits());
        Assert.Equal("rules v1\nmy own rule", OnMain("jobs/README.md"));
        var edit = Assert.Single(refresh.Edited);
        Assert.Equal(new TemplateEdit("jobs/README.md", "jobs/README.md"), edit);
        Assert.Contains(_log.Lines, l => l.Contains("jobs/README.md was edited in this project", StringComparison.Ordinal));
    }

    [Fact]
    public void Overwrite_takes_the_template_and_clears_the_entry()
    {
        Toolkit("v1", "rules v1\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "my rules\n" });
        var refresh = Refresh();
        refresh.Step();
        Assert.Single(refresh.Edited);

        var r = refresh.Overwrite("jobs/README.md");

        Assert.True(r.Ok, r.Error);
        Assert.Equal("rules v1", OnMain("jobs/README.md"));
        Assert.Empty(refresh.Edited);
        Assert.StartsWith("jobs: take the toolkit's jobs/README.md (", LastMessage(), StringComparison.Ordinal);
        Assert.False(refresh.Overwrite("jobs/project.md").Ok);
    }

    [Fact]
    public void Keep_stops_asking_until_the_template_changes_again()
    {
        Toolkit("v1", "rules v1\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "my rules\n" });
        var refresh = Refresh();
        refresh.Step();

        Assert.True(refresh.Keep("jobs/README.md"));
        Assert.Empty(refresh.Edited);

        // A scheduler started again reads the choice from the state file.
        var again = Refresh();
        again.Step();
        Assert.Empty(again.Edited);
        Assert.Equal("my rules", OnMain("jobs/README.md"));

        Toolkit("v2", "rules v2\n");
        again.Step();
        Assert.Single(again.Edited);
        Assert.Equal("my rules", OnMain("jobs/README.md"));
    }

    [Fact]
    public void The_toolkit_main_moving_triggers_a_check_and_nothing_else_does()
    {
        Toolkit("v1", "rules v1\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n" });
        var refresh = Refresh();

        refresh.Step();
        refresh.Step();
        Assert.Equal(1, refresh.Checks);

        Toolkit("v2", "rules v2\n");
        Assert.True(refresh.Step());
        Assert.Equal(2, refresh.Checks);
        Assert.Equal("rules v2", OnMain("jobs/README.md"));
    }

    [Fact]
    public void A_copy_the_scheduler_wrote_is_refreshed_even_when_that_version_is_not_in_the_history()
    {
        Toolkit("v1", "rules v1\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "old toolkit rules\n" });
        var state = new TemplateState();
        state.Of("jobs/README.md").Taken = TemplateState.Hash("old toolkit rules\n");
        state.Save(_state);

        var refresh = Refresh();
        Assert.True(refresh.Step());

        Assert.Equal("rules v1", OnMain("jobs/README.md"));
        Assert.Empty(refresh.Edited);
    }

    [Fact]
    public void A_project_still_on_docs_jobs_gets_its_copies_there()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { ["docs/jobs/README.md"] = "rules v1\n" });

        Assert.True(Refresh("docs/jobs").Step());

        Assert.Equal("rules v2", OnMain("docs/jobs/README.md"));
        Assert.DoesNotContain("jobs/README.md", GitFixture.Git(_project.Bare, "ls-tree", "-r", "--name-only", "main").Split('\n'));
    }

    [Fact]
    public void A_toolkit_without_a_remote_main_is_reported_once_and_changes_nothing()
    {
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "x\n" });
        var refresh = Refresh();

        Assert.False(refresh.Step());
        Assert.False(refresh.Step());

        Assert.Single(_log.Lines, l => l.StartsWith("templates: no origin/main", StringComparison.Ordinal));
        Assert.Equal(0, refresh.Checks);
    }

    [Fact]
    public void The_generic_files_are_what_the_installer_writes_and_never_the_project_files()
    {
        var targets = GenericFiles.For("jobs").Select(f => f.Target).ToList();

        Assert.Equal([".gitattributes", "jobs/README.md", "jobs/card-template.md", "jobs/plan-template.md", "jobs/card-template-feature.md", "jobs/card-template-bug.md", "jobs/attachments/README.md", "jobs/requests/README.md"], targets);
        foreach (var own in new[] { "project.md", "lanes.json", "workers.json", "scheduler.json", "models.json" })
        {
            Assert.DoesNotContain(targets, t => t.EndsWith(own, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_installer_still_writes_exactly_these_generic_files()
    {
        // bin/projecttoolkit.ps1 is the other half of the list: a generic file added there is added here.
        var script = File.ReadAllLines(Path.Combine(RepoRoot(), "bin", "projecttoolkit.ps1"));
        var generic = script.Select(l => l.Trim())
            .Where(l => l.StartsWith("Put ", StringComparison.Ordinal) && l.EndsWith("$true", StringComparison.Ordinal))
            .Select(l => l.Split('\'')[^2])
            .ToList();

        Assert.Equal(GenericFiles.For("jobs").Select(f => f.Template).Order(StringComparer.Ordinal), generic.Order(StringComparer.Ordinal));
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "bin", "projecttoolkit.ps1")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("bin/projecttoolkit.ps1 not found above the test's folder");
    }

    private sealed class FailingCommitter(string error) : IJobsCommitter
    {
        public int Calls { get; private set; }

        public string Error { get; set; } = error;

        public CommitResult Apply(JobsChange change)
        {
            Calls++;
            return new CommitResult { Ok = false, Error = Error };
        }
    }

    private TemplateRefresh RefreshWith(IJobsCommitter committer)
    {
        GitFixture.Git(_project.Repo, "fetch", "-q", "origin");
        return new TemplateRefresh(new GitRunner(), _toolkit.Repo, _project.Main(), committer, "jobs", _state, _log);
    }

    // 1118-B: a refresh whose commit fails is logged once and not tried again within the hour.
    [Fact]
    public void A_failing_refresh_is_logged_once_and_not_tried_again_within_the_hour()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n" });
        var committer = new FailingCommitter("commit failed: On branch master");
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var refresh = RefreshWith(committer);
        refresh.Now = () => now;

        for (var i = 0; i < 5; i++)
        {
            refresh.Step();
            now = now.AddSeconds(20);
        }

        Assert.Equal(1, committer.Calls);
        Assert.Single(_log.Lines, l => l.Contains("could not refresh", StringComparison.Ordinal));

        now = now.AddHours(1);
        refresh.Step();
        Assert.Equal(2, committer.Calls);
        Assert.Single(_log.Lines, l => l.Contains("could not refresh", StringComparison.Ordinal)); // same error: not logged again

        committer.Error = "another error";
        now = now.AddHours(1);
        refresh.Step();
        Assert.Equal(2, _log.Lines.Count(l => l.Contains("could not refresh", StringComparison.Ordinal)));
    }

    // 1118-B: the toolkit's main moving is a reason to try again before the hour is up.
    [Fact]
    public void A_failing_refresh_is_tried_again_when_the_toolkit_moves()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n" });
        var committer = new FailingCommitter("boom");
        var refresh = RefreshWith(committer);

        refresh.Step();
        refresh.Step();
        Assert.Equal(1, committer.Calls);

        Toolkit("v3", "rules v3\n");
        refresh.Step();
        Assert.Equal(2, committer.Calls);
    }

    // 1118-B: a paused project does not refresh.
    [Fact]
    public void A_paused_project_does_not_refresh()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules v1\n" });
        var committer = new FailingCommitter("boom");
        var paused = true;
        var refresh = RefreshWith(committer);
        refresh.Paused = () => paused;

        Assert.False(refresh.Step());
        Assert.Equal(0, refresh.Checks);
        Assert.Equal(0, committer.Calls);

        paused = false;
        refresh.Step();
        Assert.Equal(1, committer.Calls);
    }
}
