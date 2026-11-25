// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;
using Scheduler.Host.Locks;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1021-B, design <c>dynamic-locks.md</c> section 7 (Host): the scheduler's hooks, installed through the
/// worker's environment only, fire in a temp repository - the commit-msg hook appends the Card trailer, the
/// pre-push hook (the real <c>scheduler-next check-push</c>) refuses a push to main of an unheld path with a
/// non-zero exit, and allows a held path and any push to <c>wip/</c>.
/// </summary>
[ParallelMethods]
public sealed class HookFilesTests : IDisposable
{
    private const string Card = "1-B-hooks";
    private readonly GitFixture _fx = new();
    private readonly TempDir _log = new();
    private readonly string _hooks;

    public HookFilesTests()
    {
        _fx.Push("seed", new Dictionary<string, string?>
        {
            ["src/held/a.txt"] = "1\n",
            ["src/other/b.txt"] = "1\n",
            ["jobs/categories.json"] = "{ \"lanes\": { \"B\": { \"owns\": [\"src/\"] } }, \"shared\": { \"addOnly\": [\"docs/known_gaps.md\"] } }\n",
        });
        GitFixture.Git(_fx.Repo, "fetch", "-q", "origin");
        GitFixture.Git(_fx.Repo, "checkout", "-q", "-B", "main", "origin/main");
        var table = LockTable.Empty("C:/Projects/Hooked")
            .Start(Card, new CardAge(1, 1), Scope.Of("src/held/"), DateTimeOffset.Now.AddMinutes(-5), "p", 1);
        new LockTableFile(_log.Path, "C:/Projects/Hooked", null).Save(table);
        _hooks = Path.Combine(_log.Path, "hooks");
        Assert.True(HookFiles.Write(_hooks, Exe, null));
    }

    private static string Exe => Path.Combine(AppContext.BaseDirectory, "scheduler-next.exe");

    [Fact]
    public void Hooks_are_sh_scripts_with_lf_line_ends()
    {
        foreach (var name in new[] { HookFiles.CommitMsg, HookFiles.PrePush })
        {
            var text = File.ReadAllText(Path.Combine(_hooks, name));
            Assert.StartsWith("#!/bin/sh\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        }

        Assert.Contains("check-push", File.ReadAllText(Path.Combine(_hooks, HookFiles.PrePush)), StringComparison.Ordinal);
    }

    [Fact]
    public void Commit_msg_hook_appends_the_card_trailer()
    {
        Commit("src/held/a.txt", "2\n", "change a\n\nCo-Authored-By: t <t@t>");
        var trailer = GitFixture.Git(_fx.Repo, "log", "-1", "--format=%(trailers:key=Card,valueonly)");
        Assert.Equal(Card, trailer.Trim());
    }

    [Fact]
    public void Without_the_environment_no_hook_runs()
    {
        Assert.Equal(0, Run(null, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "plain").Exit);
        Assert.Equal("", GitFixture.Git(_fx.Repo, "log", "-1", "--format=%(trailers:key=Card,valueonly)").Trim());
    }

    [Fact]
    public void Pre_push_hook_refuses_an_unheld_path_on_main_and_allows_wip()
    {
        Commit("src/other/b.txt", "2\n", "change b");

        var main = Run(Env(), "push", "origin", "HEAD:main");
        Assert.NotEqual(0, main.Exit);
        Assert.Contains($"{Card} does not hold src/other/b.txt", main.Output, StringComparison.Ordinal);
        Assert.Contains(" lock src/other/b.txt", main.Output, StringComparison.Ordinal);

        var wip = Run(Env(), "push", "origin", $"HEAD:wip/{Card}");
        Assert.True(wip.Exit == 0, wip.Output);
    }

    [Fact]
    public void Pre_push_hook_allows_a_held_path_and_an_add_only_file()
    {
        Commit("src/held/a.txt", "3\n", "change a");
        Commit("docs/known_gaps.md", "- a gap\n", "add a gap");
        var push = Run(Env(), "push", "origin", "HEAD:main");
        Assert.True(push.Exit == 0, push.Output);
    }

    [Fact]
    public void Check_push_refuses_another_cards_card_file()
    {
        Commit("jobs/active/2-C-other.md", "# other\n", "touch another card");
        var push = Run(Env(), "push", "origin", "HEAD:main");
        Assert.NotEqual(0, push.Exit);
        Assert.Contains("may not change jobs/active/2-C-other.md", push.Output, StringComparison.Ordinal);
    }

    // Card 1039-B: with the worker environment set, an unrelated repository (a test's temp repo) is left alone:
    // no Card trailer, and a push of a path the card does not hold is not checked.
    [Fact]
    public void In_an_unrelated_repository_the_hooks_do_nothing()
    {
        using var other = new GitFixture();
        other.Push("seed", new Dictionary<string, string?> { ["src/other/b.txt"] = "1\n" });
        GitFixture.Git(other.Repo, "fetch", "-q", "origin");
        GitFixture.Git(other.Repo, "checkout", "-q", "-B", "main", "origin/main");
        File.WriteAllText(Path.Combine(other.Repo, "src", "other", "b.txt"), "2\n");
        Assert.Equal(0, RunIn(other.Repo, Env(), "add", "-A").Exit);
        var c = RunIn(other.Repo, Env(), "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "change b");
        Assert.True(c.Exit == 0, c.Output);
        Assert.Equal("", GitFixture.Git(other.Repo, "log", "-1", "--format=%(trailers:key=Card,valueonly)").Trim());

        var push = RunIn(other.Repo, Env(), "push", "origin", "HEAD:main");
        Assert.True(push.Exit == 0, push.Output);
    }

    [Fact]
    public void Without_PTK_REPO_the_hooks_do_nothing()
    {
        Commit("src/other/b.txt", "2\n", "change b");
        var env = Env();
        env.Remove("PTK_REPO");
        Assert.Equal(0, Run(env, "push", "origin", "HEAD:main").Exit);
    }

    public void Dispose()
    {
        _fx.Dispose();
        _log.Dispose();
    }

    private void Commit(string path, string text, string message)
    {
        var full = Path.Combine(_fx.Repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        Assert.Equal(0, Run(Env(), "add", "-A").Exit);
        var r = Run(Env(), "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
        Assert.True(r.Exit == 0, r.Output);
    }

    private Dictionary<string, string> Env() => new()
    {
        ["PTK_CARD"] = Card,
        ["PTK_SCHEDULER_EXE"] = Exe,
        ["PTK_LOG_DIR"] = _log.Path,
        ["PTK_REPO"] = _fx.Repo.Replace('\\', '/') + "/.git",
        ["GIT_CONFIG_COUNT"] = "1",
        ["GIT_CONFIG_KEY_0"] = "core.hooksPath",
        ["GIT_CONFIG_VALUE_0"] = _hooks,
    };

    // git in the worker repository with exactly the given lock variables (none when null).
    private (int Exit, string Output) Run(Dictionary<string, string>? env, params string[] args) => RunIn(_fx.Repo, env, args);

    private static (int Exit, string Output) RunIn(string dir, Dictionary<string, string>? env, params string[] args)
    {
        var psi = new ProcessStartInfo(Scheduler.Host.Git.GitExecutable.Path) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false }; _ = Scheduler.Host.Git.GitExecutable.Apply(psi);
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("PTK_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("GIT_CONFIG_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            psi.Environment.Remove(key);
        }

        foreach (var (k, v) in env ?? [])
        {
            psi.Environment[k] = v;
        }

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
}
