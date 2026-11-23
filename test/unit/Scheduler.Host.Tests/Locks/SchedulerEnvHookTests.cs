using System.Diagnostics;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.TestSupport;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1070-B: the git a test starts must never carry the scheduler worker's own git settings. A worker session is
/// started with <c>core.hooksPath</c> at the scheduler's hooks folder (<c>GIT_CONFIG_COUNT</c> /
/// <c>GIT_CONFIG_KEY_0</c> / <c>GIT_CONFIG_VALUE_0</c>) and the <c>PTK_*</c> variables, and every git process
/// inherits them, so every commit in a test repository runs the scheduler's hooks (about 9 s a commit with them,
/// 1.7 s without, 1041-B). The module initializer in <c>test/unit/TestSupport/SchedulerEnv.cs</c> removes them
/// from the test process at load. These tests arm a repository with a <c>commit-msg</c> hook that leaves a marker
/// in the committing repository and check both sides: with the variables in the environment the marker appears
/// (so the second test can fail), and after the load strip it does not.
/// </summary>
// 1082-A: the first test arms core.hooksPath on the whole test process for one commit; any git another class
// starts in that window inherits it and fails once this class's temp hooks folder is gone. So it runs alone.
[CollectionDefinition(nameof(SchedulerEnvHookTests), DisableParallelization = true)]
public sealed class SchedulerEnvHookTestsCollection;

[Collection(nameof(SchedulerEnvHookTests))]
public sealed class SchedulerEnvHookTests : IDisposable
{
    private const string Marker = "hook-ran";

    private readonly TempDir _temp = new();
    private readonly string _repo;
    private readonly string _hooks;
    private readonly string _ran;

    public SchedulerEnvHookTests()
    {
        _repo = Path.Combine(_temp.Path, "repo");
        Directory.CreateDirectory(_repo);
        _ = GitFixture.Git(_repo, "init", "-q", "-b", "main");
        var hooks = Path.Combine(_temp.Path, "hooks");
        Directory.CreateDirectory(hooks);
        _hooks = hooks;

        // The marker goes beside the message file git hands the hook, i.e. inside the committing repository:
        // a commit in any other repository leaves its own marker, never this test's.
        File.WriteAllText(Path.Combine(hooks, "commit-msg"), "#!/bin/sh\nd=\"$(dirname \"$1\")\"\n[ -d \"$d\" ] || d=.git\n: > \"$d/" + Marker + "\"\nexit 0\n");
        _ran = Path.Combine(_repo, ".git", Marker);
    }

    [Fact]
    public void A_commit_inheriting_the_armed_environment_runs_the_hook()
    {
        Stage("a.txt", "1\n");
        var saved = SetProcessEnv(SchedulerVars());
        try
        {
            Commit("armed by the worker environment");
        }
        finally
        {
            Restore(saved);
        }

        Assert.True(File.Exists(_ran), "the commit-msg hook did not run even with the variables set: the check below proves nothing until this one passes");
    }

    // What the module initializer did at load: the variables are gone, so the git this commit starts inherits none of them.
    [Fact]
    public void A_commit_after_the_load_strip_runs_no_hook()
    {
        Stage("b.txt", "1\n");
        var saved = SetProcessEnv(SchedulerVars());
        try
        {
            _ = SchedulerEnv.RemoveFromProcess();
            Commit("the load strip took the variables away");
        }
        finally
        {
            Restore(saved);
        }

        Assert.False(File.Exists(_ran), "a test commit ran a git hook: the test process still carries the scheduler's GIT_CONFIG_* or PTK_* variables");
    }

    [Fact]
    public void The_test_process_holds_no_scheduler_variable()
    {
        // What the initializer found at load is gone, and nothing of the kind is left for a git to inherit.
        foreach (var name in SchedulerEnv.RemovedAtLoad.Keys)
        {
            Assert.Null(Environment.GetEnvironmentVariable(name));
        }

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name)
            {
                Assert.False(SchedulerEnv.IsSchedulerVariable(name), $"{name} is still in the test process; every git a test starts inherits it");
            }
        }
    }

    [Fact]
    public void The_strip_takes_the_scheduler_variables_and_nothing_else()
    {
        foreach (var name in new[]
                 {
                     "PTK_CARD", "PTK_REPO", "PTK_JOBS", "PTK_SCHEDULER_EXE", "PTK_SLOT",
                     "GIT_CONFIG_COUNT", "GIT_CONFIG_KEY_0", "GIT_CONFIG_KEY_11", "GIT_CONFIG_VALUE_0", "GIT_CONFIG_VALUE_11", "GIT_CONFIG_PARAMETERS",
                     "git_config_count", "Ptk_Card",
                 })
        {
            Assert.True(SchedulerEnv.IsSchedulerVariable(name), name);
        }

        foreach (var name in new[]
                 {
                     "PATH", "HOME", "USERPROFILE", "MSYSTEM", "COMSPEC",
                     "GIT_DIR", "GIT_INDEX_FILE", "GIT_WORK_TREE", "GIT_CONFIG", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_ASKPASS",
                     "GIT_TERMINAL_PROMPT", "GIT_SEQ_DIR", "PTK", "PTX_JOBS", "GIT_CONFIG_KEY", "DOTNET_CLI_TELEMETRY_OPTOUT",
                 })
        {
            Assert.False(SchedulerEnv.IsSchedulerVariable(name), name);
        }
    }

    public void Dispose() => _temp.Dispose();

    // The variables this worker session is started with, as WorkerFiles.cs and WorkerLocks.cs set them.
    private Dictionary<string, string> SchedulerVars() => new()
    {
        ["GIT_CONFIG_COUNT"] = "1",
        ["GIT_CONFIG_KEY_0"] = "core.hooksPath",
        ["GIT_CONFIG_VALUE_0"] = _hooks,
        ["GIT_CONFIG_PARAMETERS"] = "'core.fsmonitor=false'",
        ["PTK_CARD"] = "1070-B-tests-drop-scheduler-hooks",
        ["PTK_REPO"] = "C:/repos/Proj/.git",
        ["PTK_JOBS"] = "5",
    };

    private void Stage(string path, string text) =>
        File.WriteAllText(Path.Combine(_repo, path), text);

    private void Commit(string message)
    {
        var add = Run("add", "-A");
        Assert.True(add.Exit == 0, add.Output);
        var c = Run("-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
        Assert.True(c.Exit == 0, c.Output);
    }

    // git with the environment this process has: nothing added, nothing removed (that is the point of these tests).
    private (int Exit, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Scheduler.Host.Git.GitExecutable.Path)
        {
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        _ = Scheduler.Host.Git.GitExecutable.Apply(psi);
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        // No input, ever: a git that reads stdin would otherwise read the terminal the tests were started from.
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        var o = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            p.Kill(true);
            return (-1, "git timed out");
        }

        p.WaitForExit();
        return (p.ExitCode, o.Result + err.Result);
    }

    private static List<(string Name, string? Value)> SetProcessEnv(IDictionary<string, string> values)
    {
        var saved = new List<(string, string?)>();
        foreach (var (name, value) in values)
        {
            saved.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }

        return saved;
    }

    private static void Restore(List<(string Name, string? Value)> saved)
    {
        foreach (var (name, value) in saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
