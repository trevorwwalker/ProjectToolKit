// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// Card 1302-B: <c>--help</c>, <c>-h</c>, <c>-?</c> and <c>/?</c> print the usage and exit 0 before anything
/// else is read or written.
/// </summary>
public class HelpSwitchTests
{
    private static readonly string[] Names =
    [
        "--version", "--deliver-message", "lock", "check-push", "-Pool", "-PoolName", "<repository>", "-Repo",
        "-Config", "-Slots", "-Port", "-Model", "-NoHealthCheck", "-Paused", "-DryRun", "-NoBrowser",
        "-ShutdownEvent", "-Replaces", "docs/guide/scheduler-cli.md",
    ];

    private static (int Code, string Out, string Err) Run(string workDir, params string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "scheduler-next.exe");
        Assert.True(File.Exists(exe), $"scheduler-next.exe is not beside the tests ({exe})");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = workDir,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("scheduler-next did not start");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60_000), "scheduler-next did not exit in 60 s");
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    private static string Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "help-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("--HELP")]
    public void Help_PrintsTheUsageAndExitsZero_WritingNothing(string spelling)
    {
        var dir = Scratch();
        try
        {
            var (code, stdout, stderr) = Run(dir, spelling);

            Assert.Equal(0, code);
            Assert.Equal("", stderr);
            Assert.StartsWith("scheduler-next " + CoreInfo.Version, stdout);
            foreach (var name in Names)
            {
                Assert.Contains(name, stdout);
            }

            Assert.DoesNotContain("locks:", stdout);
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Help_AmongOtherArguments_StillPrintsTheUsage()
    {
        var dir = Scratch();
        try
        {
            var (code, stdout, _) = Run(dir, dir, "-Slots", "2", "-H");

            Assert.Equal(0, code);
            Assert.Contains("usage", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Version_StillPrintsOnlyTheVersion()
    {
        var dir = Scratch();
        try
        {
            var (code, stdout, _) = Run(dir, "--version");

            Assert.Equal(0, code);
            Assert.Equal(CoreInfo.Version, stdout.Trim());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
