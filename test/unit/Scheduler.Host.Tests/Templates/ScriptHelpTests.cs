// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Xunit;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// 1271-D: PowerShell does not recognise a script's comment-based help when comment lines sit directly before the
/// <c>&lt;#</c>, so <c>Get-Help</c> printed only the syntax line. The licence lines go after the help block's
/// <c>#&gt;</c>; every script in <c>bin\</c> that has a help block must have its help recognised, and keep both lines.
/// </summary>
public class ScriptHelpTests
{
    private const string LicenceLine1 =
        "# Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.";

    private const string LicenceLine2 =
        "# Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)";

    [FactWhenPowerShellExists]
    public void Every_script_with_a_help_block_has_its_help_recognised_by_powershell()
    {
        var powershell = FindOnPath("powershell.exe")!;

        var bin = Path.Combine(RepoRoot(), "bin");
        var scripts = Directory.GetFiles(bin, "*.ps1")
            .Where(f => File.ReadAllText(f).Contains("<#", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.NotEmpty(scripts);

        var list = string.Join(",", scripts.Select(f => "'" + f.Replace("'", "''") + "'"));
        var command =
            "$bad=@(); foreach($p in @(" + list + ")){ $t=$null;$e=$null; " +
            "$h=[System.Management.Automation.Language.Parser]::ParseFile($p,[ref]$t,[ref]$e).GetHelpContent(); " +
            "if($null -eq $h -or [string]::IsNullOrWhiteSpace($h.Synopsis)){ $bad += [IO.Path]::GetFileName($p) } }; " +
            "Write-Output ('BAD:' + ($bad -join ','))";

        var psi = new ProcessStartInfo(powershell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("powershell.exe did not finish within 60 s");
        }

        var output = stdout.GetAwaiter().GetResult();
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("BAD:", StringComparison.Ordinal));
        Assert.True(line is not null, "no result from powershell.exe: " + output + stderr.GetAwaiter().GetResult());
        Assert.Equal("BAD:", line);
    }

    [Fact]
    public void Every_script_still_has_both_licence_lines()
    {
        var missing = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot(), "bin"), "*.ps1"))
        {
            var lines = File.ReadAllLines(f);
            if (!lines.Any(l => l.TrimStart('﻿') == LicenceLine1) || !lines.Any(l => l == LicenceLine2))
            {
                missing.Add(Path.GetFileName(f));
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>A fact that is skipped, not failed, when <c>powershell.exe</c> is not on the machine.</summary>
    private sealed class FactWhenPowerShellExistsAttribute : FactAttribute
    {
        public FactWhenPowerShellExistsAttribute()
        {
            if (FindOnPath("powershell.exe") is null)
            {
                Skip = "powershell.exe is not on this machine";
            }
        }
    }

    private static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir, exe);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
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
