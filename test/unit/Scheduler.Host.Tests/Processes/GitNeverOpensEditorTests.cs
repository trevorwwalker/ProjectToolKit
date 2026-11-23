using System.Diagnostics;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Slots;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Processes;

public class GitNeverOpensEditorTests
{
    // 1073-B: a worker's environment carries the non-interactive editor settings, so a git commit with no message
    // returns at once with git's "empty message" error instead of opening vi and waiting for days.
    [Fact]
    public void Worker_GitCommitWithoutMessage_ReturnsAtOnce()
    {
        using var dir = new TempDir();
        var repo = Path.Combine(dir.Path, "repo");
        Directory.CreateDirectory(repo);
        var git = GitExecutable.Path;
        Run(git, repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "a");
        Run(git, repo, "add", "a.txt");

        var outFile = Path.Combine(dir.Path, "out.txt");
        var exitFile = Path.Combine(dir.Path, "exit.txt");
        var processes = new WorkerProcesses(new JobObjects());
        var job = JobObjects.Template("t1073", "slot", 9);
        // An editor that would wait for two minutes; the worker environment's GIT_EDITOR must win over it, and over the same in the scheduler's own environment.
        var env = new Dictionary<string, string?> { ["EDITOR"] = "ping -n 120 127.0.0.1", ["VISUAL"] = "ping -n 120 127.0.0.1" };
        var script = Path.Combine(dir.Path, "commit.cmd");
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            $"\"{git}\" -c user.name=t -c user.email=t@t commit > \"{outFile}\" 2>&1",
            $"echo %errorlevel% > \"{exitFile}\"",
            ""));
        var line = $"\"{script}\"";
        var hostile = "ping -n 120 127.0.0.1";
        var saved = Environment.GetEnvironmentVariable("GIT_EDITOR");
        Environment.SetEnvironmentVariable("GIT_EDITOR", hostile); // the scheduler's own environment may carry any editor
        StartedProcess? started;
        try
        {
            started = processes.Start(line, job, repo, env);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_EDITOR", saved);
        }
        Assert.NotNull(started);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(exitFile) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
            Assert.True(File.Exists(exitFile), "git commit did not return: it is waiting on an editor");
            Assert.NotEqual("0", File.ReadAllText(exitFile).Trim());
            Assert.Contains("empty", File.ReadAllText(outFile), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            processes.EndJob(job, started!.Pid);
        }
    }

    // 1073-B: the scheduler's own git (GitRunner) has the three settings too.
    [Fact]
    public void GitRunner_EnvironmentHasNonInteractiveEditor()
    {
        using var temp = new TempDir();
        var fake = Path.Combine(temp.Path, "fakegit.cmd");
        var record = Path.Combine(temp.Path, "record.txt");
        File.WriteAllText(fake, string.Join("\r\n",
            "@echo off",
            $">>\"{record}\" echo E=%GIT_EDITOR% S=%GIT_SEQUENCE_EDITOR% M=%GIT_MERGE_AUTOEDIT%",
            "exit /b 0",
            ""));

        new GitRunner(fake, TimeSpan.FromSeconds(30)).Run(temp.Path, "status");

        Assert.Contains("E=true S=true M=no", File.ReadAllText(record));
    }

    private static void Run(string exe, string dir, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        GitExecutable.Apply(psi);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
    }
}
