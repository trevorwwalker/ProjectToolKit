// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Microsoft.Win32;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 1266-B: bin\test-installer.ps1 refuses to start over an earlier test install that is still registered, compares
/// the Switchyard processes it recorded before the install with those after the uninstall, and bin\publish-release.ps1
/// passes it -AllowRunningSwitchyard. The script is never given a real installer here.
/// </summary>
public class TestInstallerScriptTests
{
    private const string Before = "101,2026-11-24T10:00:00.0000000Z,C:/kit/scheduler-next.exe|102,2026-11-24T10:01:00.0000000Z,C:/kit/scheduler-tray.exe";

    [Fact]
    public void The_script_refuses_while_an_earlier_test_install_is_registered()
    {
        var name = $@"Software\ProjectToolkit-test\{Guid.NewGuid():N}";
        using (var key = Registry.CurrentUser.CreateSubKey(name))
        {
            key.SetValue("InstallLocation", @"C:\old-test-install\app");
        }

        try
        {
            var setup = Path.Combine(Path.GetTempPath(), $"ptk-setup-{Guid.NewGuid():N}.exe");
            File.WriteAllText(setup, "not an installer");
            try
            {
                var (code, output) = Run("-Setup", setup, "-TestUninstallKey", @"HKCU:\" + name);

                Assert.Equal(1, code);
                Assert.Contains("test-installer: refused: an earlier test install is still registered", output, StringComparison.Ordinal);
                Assert.Contains(name, output, StringComparison.Ordinal);
                Assert.Contains(@"C:\old-test-install\app", output, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(setup);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void The_survivors_comparison_passes_for_the_same_processes()
    {
        var (code, output) = Run("-SelfTest", "-SelfTestBefore", Before, "-SelfTestAfter", Before);

        Assert.Equal(0, code);
        Assert.Contains("the Switchyard that was running is still running: 2 process(es)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_survivors_comparison_fails_naming_a_missing_process()
    {
        var after = "101,2026-11-24T10:00:00.0000000Z,C:/kit/scheduler-next.exe";

        var (code, output) = Run("-SelfTest", "-SelfTestBefore", Before, "-SelfTestAfter", after);

        Assert.Equal(1, code);
        Assert.Contains("FAIL", output, StringComparison.Ordinal);
        Assert.Contains("1 of 2 process(es) missing", output, StringComparison.Ordinal);
        Assert.Contains("102 C:/kit/scheduler-tray.exe", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_survivors_comparison_fails_when_the_id_was_reused_with_another_start_time()
    {
        var after = "101,2026-11-24T11:00:00.0000000Z,C:/kit/scheduler-next.exe|102,2026-11-24T10:01:00.0000000Z,C:/kit/scheduler-tray.exe";

        var (code, output) = Run("-SelfTest", "-SelfTestBefore", Before, "-SelfTestAfter", after);

        Assert.Equal(1, code);
        Assert.Contains("101 C:/kit/scheduler-next.exe", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_survivors_failure_names_the_missing_process_with_its_command_line()
    {
        var before = "201,2026-11-24T10:00:00.0000000Z,C:/kit/scheduler-next.exe,\"C:/kit/scheduler-next.exe\" pool --port 1234";

        var (code, output) = Run("-SelfTest", "-SelfTestBefore", before, "-SelfTestAfter", "");

        Assert.Equal(1, code);
        Assert.Contains("201 C:/kit/scheduler-next.exe [\"C:/kit/scheduler-next.exe\" pool --port 1234]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_calls_that_are_gone_afterwards_were_never_recorded_and_pass()
    {
        const string exe = "C:/kit/scheduler-next.exe";
        var before = string.Join('|',
            $"301,2026-11-24T10:00:00.0000000Z,{exe},\"{exe}\" lock src/a.cs --why x",
            $"302,2026-11-24T10:00:01.0000000Z,{exe},{exe} check-push",
            $"303,2026-11-24T10:00:02.0000000Z,{exe},\"{exe}\" --deliver-message claude",
            $"304,2026-11-24T10:00:03.0000000Z,{exe},\"{exe}\" --version",
            "305,,,");

        var (code, output) = Run("-SelfTest", "-SelfTestBefore", before, "-SelfTestAfter", "");

        Assert.Equal(0, code);
        Assert.Contains("is still running: 0 process(es)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scheduler_and_a_tray_are_recorded_and_pass_when_still_running()
    {
        const string exe = "C:/kit/scheduler-next.exe";
        var list = $"401,2026-11-24T10:00:00.0000000Z,{exe},\"{exe}\" pool|402,2026-11-24T10:00:01.0000000Z,C:/kit/scheduler-tray.exe,\"C:/kit/scheduler-tray.exe\"";

        var (code, output) = Run("-SelfTest", "-SelfTestBefore", list, "-SelfTestAfter", list);

        Assert.Equal(0, code);
        Assert.Contains("is still running: 2 process(es)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scheduler_with_an_unknown_command_line_is_recorded()
    {
        var before = "501,2026-11-24T10:00:00.0000000Z,C:/kit/scheduler-next.exe,";

        var (code, output) = Run("-SelfTest", "-SelfTestBefore", before, "-SelfTestAfter", "");

        Assert.Equal(1, code);
        Assert.Contains("1 of 1 process(es) missing", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_script_passes_the_switch_to_the_installer_test()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "bin", "publish-release.ps1"));
        var line = text.Split('\n').Single(l => l.Contains("'test-installer.ps1'", StringComparison.Ordinal) && l.Contains("Invoke-Logged", StringComparison.Ordinal));

        Assert.Contains("'-AllowRunningSwitchyard'", line, StringComparison.Ordinal);
    }

    private static (int Code, string Output) Run(params string[] args)
    {
        var start = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot(), "bin", "test-installer.ps1") }.Concat(args))
        {
            start.ArgumentList.Add(arg);
        }

        using var p = Process.Start(start)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "test-installer.ps1 did not finish in 60 s");
        Assert.Equal("", stderr.Result);
        return (p.ExitCode, output);
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
}
