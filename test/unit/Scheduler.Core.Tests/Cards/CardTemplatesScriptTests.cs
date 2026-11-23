using System.Diagnostics;

namespace Scheduler.Core.Tests.Cards;

/// <summary>
/// 1030-A: <c>bin/projecttoolkit.ps1</c> creates jobs/attachments/ and copies the feature and bug
/// templates, for the <c>jobs</c> action and again for <c>-Update</c> when they are missing; and the
/// project's own copies match the kit's.
/// </summary>
public class CardTemplatesScriptTests
{
    private static readonly string[] Files =
    [
        "card-template.md", "card-template-feature.md", "card-template-bug.md", "attachments/README.md",
    ];

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

    private static string Lf(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static void Run(string kit, string repo, params string[] extra) => Run(kit, repo, null, extra);

    /// <summary>
    /// Runs <c>bin/projecttoolkit.ps1 -Action jobs</c> on <paramref name="repo"/>. An entry with a null value
    /// in <paramref name="env"/> is removed from the child powershell's environment, so a test can prove which
    /// of the script's guards did the work rather than inheriting one it did not intend to use.
    /// </summary>
    private static void Run(string kit, string repo, Dictionary<string, string?>? env, params string[] extra)
    {
        var (output, exitCode) = Invoke(kit, repo, env, extra);
        Assert.True(exitCode == 0, output);
    }

    /// <summary>Starts the script and waits, and hands back everything it printed and its exit code.</summary>
    private static (string Output, int ExitCode) Invoke(string kit, string repo, Dictionary<string, string?>? env, params string[] extra)
    {
        var start = new ProcessStartInfo("powershell")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-NoProfile", "-File", Path.Combine(kit, "bin", "projecttoolkit.ps1"), "-Action", "jobs", "-Repo", repo })
        {
            start.ArgumentList.Add(a);
        }

        foreach (var a in extra)
        {
            start.ArgumentList.Add(a);
        }

        if (env is not null)
        {
            foreach (var (name, value) in env)
            {
                if (value is null)
                {
                    _ = start.Environment.Remove(name);
                }
                else
                {
                    start.Environment[name] = value;
                }
            }
        }

        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var errors = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(90_000), "projecttoolkit.ps1 did not finish in 90 s");
        return (output.Result + errors.Result, p.ExitCode);
    }

    [Fact]
    public void Jobs_CreatesAttachmentsAndTemplates_AndUpdateRestoresMissingOnes()
    {
        var kit = KitRoot();
        var repo = Scheduler.TestSupport.TestTmp.NewPath("ptk-1030");
        Directory.CreateDirectory(repo);
        try
        {
            // -NoPool: this is a scratch repository, never a project; the pool is the live scheduler's (1088-B).
            Run(kit, repo, "-NoPool");
            foreach (var f in Files)
            {
                Assert.Equal(Lf(Path.Combine(kit, "assets", "templates", "jobs", f)), Lf(Path.Combine(repo, "jobs", f)));
            }

            Directory.Delete(Path.Combine(repo, "jobs", "attachments"), true);
            File.Delete(Path.Combine(repo, "jobs", "card-template-bug.md"));
            Run(kit, repo, "-NoPool", "-Update");
            foreach (var f in Files)
            {
                Assert.True(File.Exists(Path.Combine(repo, "jobs", f)), f);
            }
        }
        finally
        {
            DeleteBestEffort(repo);
        }
    }

    /// <summary>
    /// 1088-B: a repository under TestTmp's <c>out\test-tmp\</c> is a test's scratch folder, never a project.
    /// Neither of the script's other guards can be counted on for one: <c>SchedulerEnv</c> strips the
    /// scheduler's <c>PTK_*</c> variables from the test process (1070-B), and such a folder is not under the
    /// temp folder (1042-B moved it into the repository), so before this the test repositories joined the live
    /// pool by the dozen. Runs the jobs action without <c>-NoPool</c> against a scratch LOCALAPPDATA and
    /// requires the pool file there to be left exactly as it was, with the test-scratch reason printed.
    /// </summary>
    [Fact]
    public void Jobs_WithoutNoPool_TestTmpRepo_LeavesTheScratchPoolUnchanged()
    {
        var kit = KitRoot();
        var repo = Scheduler.TestSupport.TestTmp.NewPath("ptk-1030-guard");
        Directory.CreateDirectory(repo);
        var local = Scheduler.TestSupport.TestTmp.Create("ptk-1030-local");
        var poolDir = Path.Combine(local, "ProjectToolkit");
        Directory.CreateDirectory(poolDir);
        var pool = Path.Combine(poolDir, "pool.json");
        var seed = string.Join(
            "\n",
            [
                "{",
                "  \"about\": \"The scheduler's pool.\",",
                "  \"projects\": [",
                "    { \"name\": \"Alpha\", \"repo\": \"C:\\\\Projects\\\\Alpha\", \"weight\": 50 }",
                "  ],",
                "  \"port\": 8766",
                "}",
                "",
            ]);
        File.WriteAllText(pool, seed);

        // jobs/scheduler.json exists before the run, so the only reason the join can be skipped is the folder
        // guard; and the worker variables are taken out of the child on purpose, so they cannot be the reason.
        Directory.CreateDirectory(Path.Combine(repo, "jobs"));
        File.WriteAllText(Path.Combine(repo, "jobs", "scheduler.json"), "{ \"project\": \"scratch\" }");

        try
        {
            var (output, exitCode) = Invoke(
                kit,
                repo,
                new Dictionary<string, string?>
                {
                    ["LOCALAPPDATA"] = local,
                    ["PTK_CARD"] = null,
                    ["PTK_JOBS"] = null,
                    ["AGENT_JOBS"] = null,
                });
            Assert.True(exitCode == 0, output);
            Assert.Matches("skipped\\s+the pool join: .*test scratch folder", output);
            Assert.Equal(seed, File.ReadAllText(pool));
        }
        finally
        {
            DeleteBestEffort(repo);
        }
    }

    /// <summary>
    /// 1072-B: the script's child processes (or a file scanner) can still hold the scratch folder for a
    /// moment after powershell exits, so a bare Directory.Delete here failed a passing test with a sharing
    /// violation on the health check. Retry briefly, then leave the folder to TestTmp's exit sweep.
    /// </summary>
    private static void DeleteBestEffort(string dir)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(dir, true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 9)
                {
                    return;
                }

                Thread.Sleep(200);
            }
        }
    }

    [Fact]
    public void FeatureAndBugTemplates_HaveTheirTypeAndHeaderFields()
    {
        var kit = KitRoot();
        var feature = Lf(Path.Combine(kit, "assets", "templates", "jobs", "card-template-feature.md"));
        var bug = Lf(Path.Combine(kit, "assets", "templates", "jobs", "card-template-bug.md"));
        Assert.Contains("\nType: feature\n", feature);
        Assert.Contains("\nType: bug\nPlan:", bug);
        Assert.Contains("\nVersion: <e.g. 3.0.61, or the commit>\n", bug);
        Assert.EndsWith("## Questions\n\n## Result\n", feature);
        Assert.EndsWith("## Questions\n\n## Result\n", bug);
    }
}
