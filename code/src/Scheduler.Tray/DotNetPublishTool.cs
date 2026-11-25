// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;

namespace Scheduler.Tray;

/// <summary>
/// <see cref="IPublishTool"/> that runs <c>dotnet publish</c> for real (967-B, BUILD-2): the Host project
/// into <c>out\windows\scheduler.next</c>, which brings the tray and the themes with it. Because the
/// running scheduler is never written to, the publish needs no stop and cannot fight a locked file.
/// </summary>
public sealed class DotNetPublishTool(Action<string>? log = null) : IPublishTool
{
    /// <summary>How long a publish is given before it is called a failure.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    public async Task<bool> PublishAsync(string project, string outputDir, CancellationToken cancel)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = SchedulerProcess.LogEncoding,
            WorkingDirectory = Path.GetDirectoryName(project) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in new[] { "publish", project, "-c", "Release", "-o", outputDir, "-v", "quiet", "--nologo" })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var errors = new List<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (errors)
                {
                    errors.Add(e.Data.Trim());
                }
            }
        };

        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
        try
        {
            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            Say($"publish: {e.Message}");
            return false;
        }

        if (process.ExitCode != 0)
        {
            Say($"publish: dotnet publish exited with {process.ExitCode}");
            foreach (var line in Last(errors, 10))
            {
                Say($"publish: {line}");
            }

            return false;
        }

        return true;
    }

    private static string[] Last(List<string> lines, int count)
    {
        lock (lines)
        {
            return lines.Count <= count ? [.. lines] : [.. lines[(lines.Count - count)..]];
        }
    }

    private void Say(string line) => log?.Invoke(line);
}
