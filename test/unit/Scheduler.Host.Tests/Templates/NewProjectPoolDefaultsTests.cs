// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using System.Text.Json;

namespace Scheduler.Host.Tests.Templates;

/// <summary>
/// 1182-B: what <c>bin/projecttoolkit.ps1</c> writes for a new project works with only the guide's required
/// installs: a pool file whose default worker type runs the Copilot CLI on GitHub's own models, and a
/// <c>scheduler.json</c> with no model server address in it.
/// </summary>
public sealed class NewProjectPoolDefaultsTests
{
    private static string KitRoot()
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

    [Fact]
    public async Task NewPoolFile_DefaultsToCopilotOnGithubModels_WithNoLocalServer()
    {
        var kit = KitRoot();
        var local = Scheduler.TestSupport.TestTmp.Create("ptk-1182-local");

        // The repository is under the temp folder (not out\test-tmp\, which the script never joins a pool from),
        // and -JoinPool forces the join past the temp-folder guard; the pool file is in the scratch LOCALAPPDATA.
        var repo = Path.Combine(Path.GetTempPath(), "ptk-1182-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(repo);
        try
        {
            var start = new ProcessStartInfo("powershell") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-NoProfile", "-File", Path.Combine(kit, "bin", "projecttoolkit.ps1"), "-Action", "jobs", "-Repo", repo, "-Project", "Scratch", "-JoinPool" })
            {
                start.ArgumentList.Add(a);
            }

            start.Environment["LOCALAPPDATA"] = local;
            _ = start.Environment.Remove("PTK_CARD");
            _ = start.Environment.Remove("PTK_JOBS");
            _ = start.Environment.Remove("AGENT_JOBS");
            using var p = Process.Start(start)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var errors = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(90_000), "projecttoolkit.ps1 did not finish in 90 s");
            Assert.True(p.ExitCode == 0, await output + await errors);

            var poolText = File.ReadAllText(Path.Combine(local, "ProjectToolkit", "pool.json"));
            Assert.DoesNotContain("spark", poolText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("premium", poolText, StringComparison.OrdinalIgnoreCase);
            using var poolDoc = JsonDocument.Parse(poolText);
            var pool = poolDoc.RootElement.GetProperty("pool");
            var types = pool.GetProperty("types");
            var defaultName = pool.GetProperty("defaultWorker").GetString()!;
            Assert.True(types.TryGetProperty(defaultName, out var defaultType), "defaultWorker names no listed type");
            Assert.Equal("", defaultType.GetProperty("agent").GetString());
            Assert.True(defaultType.GetProperty("max").GetInt32() > 0);
            Assert.Equal("claude", types.GetProperty("Claude").GetProperty("agent").GetString());

            var config = File.ReadAllText(Path.Combine(repo, "jobs", "scheduler.json"));
            Assert.DoesNotContain("spark", config, StringComparison.OrdinalIgnoreCase);
            using var configDoc = JsonDocument.Parse(config);
            var worker = configDoc.RootElement.GetProperty("worker");
            Assert.Equal("copilot", worker.GetProperty("cli").GetString());
            Assert.Equal("", worker.GetProperty("endpoint").GetString());
            Assert.DoesNotContain("--model={model}", worker.GetProperty("args").ToString());
        }
        finally
        {
            try
            {
                Directory.Delete(repo, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
