// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 1479-B: bin\deploy-scheduler.ps1 takes for "the scheduler" only a scheduler-next.exe that is not a worker's short
/// call (lock, check-push, --deliver-message, --version, --help). Run in -SelfTest mode on a list of fake processes:
/// no deploy runs and nothing is stopped or started.
/// </summary>
public class DeploySchedulerScriptTests
{
    private const string Exe = "\"" + @"C:\k\out\windows\scheduler\scheduler-next.exe" + "\"";

    [Theory]
    [InlineData("lock Game/src/a.cs --why \"edit\"")]
    [InlineData("check-push")]
    [InlineData("--deliver-message msg.txt")]
    [InlineData("--version")]
    [InlineData("--help")]
    public void A_short_call_is_never_the_scheduler(string arguments)
    {
        Assert.Equal("", Schedulers($"1,{Exe} {arguments}"));
    }

    [Fact]
    public void A_pool_scheduler_is_the_scheduler()
    {
        Assert.Equal("7", Schedulers($"3,{Exe} lock a.cs --why x|7,{Exe} -Pool|8,{Exe} check-push"));
    }

    [Fact]
    public void A_repository_scheduler_with_an_unquoted_exe_is_the_scheduler()
    {
        Assert.Equal("4", Schedulers(@"4,C:\k\scheduler-next.exe C:\Projects\Game -Slots 2"));
    }

    [Fact]
    public void An_unreadable_command_line_counts_as_a_scheduler()
    {
        Assert.Equal("5", Schedulers("5,|6," + Exe + " --version"));
    }

    private static string Schedulers(string processes)
    {
        var start = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot(), "bin", "deploy-scheduler.ps1"),
            "-Kit", @"C:\k", "-SelfTest", "-SelfTestProcesses", processes,
        })
        {
            start.ArgumentList.Add(arg);
        }

        using var p = Process.Start(start)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "deploy-scheduler.ps1 did not finish in 60 s");
        Assert.Equal("", stderr.Result);
        Assert.Equal(0, p.ExitCode);
        const string prefix = "schedulers:";
        return output.Trim().Substring(prefix.Length).Trim();
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

        throw new InvalidOperationException("the repository root was not found");
    }
}
