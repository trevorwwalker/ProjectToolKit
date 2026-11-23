using System.Diagnostics;
using System.Text;
using Scheduler.Core;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// Card 951-A and CFG-18: <c>scheduler-next --version</c> prints the one version the build carries -
/// <see cref="CoreInfo.Version"/> - the same string the page's Control title and the tray's start
/// line show. The number itself is written once, as <c>&lt;Version&gt;</c> in
/// code/src/Directory.Build.props, so nothing here quotes it.
/// test-plans/config.md.
/// </summary>
public class VersionSwitchTests
{
    // CFG-18 · the exe's own answer is the build's version, with no source sha on the end.
    [Fact]
    public void VersionSwitch_PrintsTheBuildsVersion()
    {
        Assert.Equal(CoreInfo.Version, SchedulerVersion.Text);

        var exe = Path.Combine(AppContext.BaseDirectory, "scheduler-next.exe");
        Assert.True(File.Exists(exe), $"scheduler-next.exe is not beside the tests ({exe})");

        var psi = new ProcessStartInfo(exe, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("scheduler-next --version did not start");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Assert.True(process.WaitForExit(60_000), "scheduler-next --version did not exit in 60 s");

        // 947-B: with asynchronous reads, the timed WaitForExit returns as soon as the process is gone,
        // before the OutputDataReceived callbacks have all run; under a loaded machine (several workers
        // building at once) stdout was read empty. The untimed wait drains the readers.
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(CoreInfo.Version, stdout.ToString().Trim());
    }
}
