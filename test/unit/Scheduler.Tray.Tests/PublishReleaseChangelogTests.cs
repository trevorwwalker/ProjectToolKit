// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using System.Text;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 1485-B: bin\publish-release.ps1 drafts a changelog section from the cards finished since the last release, opens it
/// in the developer's editor, puts the result on top of CHANGELOG.md in the release folder, and records the main commit the
/// release was made from. The script runs with -ChangelogDraft / -ChangelogOnly, against a fixture repository and a scratch
/// release worktree under a temporary folder: no installer is built, nothing is pushed, gh is never called, and the real
/// release folder is never named.
/// </summary>
public class PublishReleaseChangelogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"ptk-changelog-{Guid.NewGuid():N}");
    private readonly string repo;
    private readonly string releaseDir;
    private readonly string scratchTemp;
    private readonly Dictionary<string, string> commits = new();

    public PublishReleaseChangelogTests()
    {
        repo = Path.Combine(root, "repo");
        releaseDir = Path.Combine(root, "release");
        scratchTemp = Path.Combine(root, "temp");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(scratchTemp);
        BuildFixture();
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_draft_puts_each_card_in_its_list_from_the_first_commit_above_the_published_version()
    {
        var (code, output) = RunScript("-ChangelogDraft");

        Assert.Equal(0, code);
        Assert.Contains("the first commit above version 1.0.0", output, StringComparison.Ordinal);
        var draft = output[output.IndexOf("## 1.1.0", StringComparison.Ordinal)..].Replace("\r\n", "\n");
        var expected =
            "## 1.1.0 (" + DateTime.Now.ToString("yyyy-MM-dd") + ")\n\n" +
            "### New\n- Add the thing (10-A)\n- Untyped one (14-E)\n\n" +
            "### Fixed\n- Fix the bug (11-B)\n\n" +
            "### Changed (may need action)\n\n" +
            "### No code change (delete what users do not need)\n- Docs only (12-C)\n- No bump (13-D)\n";
        Assert.Equal(expected.Trim(), draft.Trim());
        Assert.DoesNotContain("Old one", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangelogSince_overrides_the_fallback_and_lists_the_cards_finished_after_that_commit()
    {
        var (code, output) = RunScript("-ChangelogDraft", "-ChangelogSince", commits["base"]);

        Assert.Equal(0, code);
        Assert.Contains("-ChangelogSince " + commits["base"][..8], output, StringComparison.Ordinal);
        Assert.Contains("- Old one (9-Z)", output, StringComparison.Ordinal);
        Assert.True(
            output.IndexOf("(9-Z)", StringComparison.Ordinal) < output.IndexOf("(10-A)", StringComparison.Ordinal),
            "oldest first");
    }

    [Fact]
    public void An_empty_New_and_Fixed_keep_their_headings()
    {
        var (code, output) = RunScript("-ChangelogDraft", "-ChangelogSince", "main");

        Assert.Equal(0, code);
        var text = output.Replace("\r\n", "\n");
        Assert.Contains("### New\n\n### Fixed\n\n### Changed (may need action)\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No code change", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_supplied_section_lands_on_top_of_the_existing_changelog_and_the_source_commit_is_recorded()
    {
        var section = Write("section.md", "## 1.1.0 (2026-01-02)\n\n### New\n- Hand written (1-A)\n");

        var (code, output) = RunScript("-ChangelogOnly", "-ChangelogFile", section);

        Assert.Equal(0, code);
        var changelog = File.ReadAllText(Path.Combine(releaseDir, "CHANGELOG.md")).Replace("\r\n", "\n");
        Assert.Equal(
            "# ProjectToolkit changelog\n\n## 1.1.0 (2026-01-02)\n\n### New\n- Hand written (1-A)\n\n## 1.0.0 (2025-12-01)\n\n### New\n- The first release\n",
            changelog);
        Assert.Contains("Changelog-source: " + commits["head"], output, StringComparison.Ordinal);
        Assert.Contains("From main at " + commits["head"][..8] + ".", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_next_run_starts_from_the_commit_the_last_release_recorded()
    {
        var section = Write("section.md", "## 1.1.0 (2026-01-02)\n\n### New\n- Hand written (1-A)\n");
        var (code, output) = RunScript("-ChangelogOnly", "-ChangelogFile", section);
        Assert.Equal(0, code);
        // The release commit as the script words it.
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var at = Array.IndexOf(lines, "Release commit message:");
        Assert.True(at >= 0);
        Git(releaseDir, "add", "-A");
        Git(releaseDir, "commit", "-q", "-m", lines[at + 1], "-m", string.Join("\n", lines[(at + 2)..(at + 4)]));
        // Main moves on by one finished card.
        Commit(repo, "late.txt", "x", "code/src/late.txt", "code: late\n\nCard: 15-F-late");
        Commit(repo, "jobs/done/15-F-late.md", Card("15-F", "Late one", "feature", "done"), null, "jobs: done 15-F-late");

        var (code2, output2) = RunScript("-ChangelogDraft");

        Assert.Equal(0, code2);
        Assert.Contains("the commit recorded in the last release, " + commits["head"][..8], output2, StringComparison.Ordinal);
        Assert.Contains("- Late one (15-F)", output2, StringComparison.Ordinal);
        Assert.DoesNotContain("(14-E)", output2, StringComparison.Ordinal);
        Assert.DoesNotContain("(10-A)", output2, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unchanged_draft_asks_and_a_no_stops_leaving_the_file_and_the_release_folder_as_found()
    {
        var editor = Write("noop.cmd", "@exit /b 0\r\n");

        var (code, output) = RunScript(["-ChangelogOnly"], stdin: "n\n", editor: editor);

        Assert.Equal(1, code);
        Assert.Contains("Publish this changelog as it is? [y/N]", output, StringComparison.Ordinal);
        Assert.Contains("nothing was committed or pushed", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(releaseDir, "CHANGELOG.md.tmp")));
        Assert.Contains("The first release", File.ReadAllText(Path.Combine(releaseDir, "CHANGELOG.md")), StringComparison.Ordinal);
        Assert.DoesNotContain("1.1.0", File.ReadAllText(Path.Combine(releaseDir, "CHANGELOG.md")), StringComparison.Ordinal);
        Assert.Equal("", Git(releaseDir, "status", "--porcelain").Trim());
        Assert.True(File.Exists(SavedDraft()), "the draft stays where it is");
    }

    [Fact]
    public void A_second_run_offers_the_saved_draft_and_an_edited_one_is_published_without_asking()
    {
        var noop = Write("noop.cmd", "@exit /b 0\r\n");
        RunScript(["-ChangelogOnly"], stdin: "n\n", editor: noop);
        var edited = Write("edited.md", "## 1.1.0 (2026-01-03)\r\n\r\n### New\r\n- Edited by hand (2-A)\r\n");
        var edit = Write("edit.cmd", $"@copy /y \"{edited}\" %1 >nul\r\n");

        var (code, output) = RunScript(["-ChangelogOnly"], stdin: "y\n", editor: edit);

        Assert.Equal(0, code);
        Assert.Contains("already saved at", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Publish this changelog as it is?", output, StringComparison.Ordinal);
        var changelog = File.ReadAllText(Path.Combine(releaseDir, "CHANGELOG.md")).Replace("\r\n", "\n");
        Assert.StartsWith("# ProjectToolkit changelog\n\n## 1.1.0 (2026-01-03)\n\n### New\n- Edited by hand (2-A)\n\n## 1.0.0", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_that_still_has_the_delete_heading_asks_and_a_yes_publishes_it()
    {
        var noop = Write("noop.cmd", "@exit /b 0\r\n");

        var (code, output) = RunScript(["-ChangelogOnly"], stdin: "y\n", editor: noop);

        Assert.Equal(0, code);
        Assert.Contains("Publish this changelog as it is? [y/N]", output, StringComparison.Ordinal);
        var changelog = File.ReadAllText(Path.Combine(releaseDir, "CHANGELOG.md"));
        Assert.Contains("- Add the thing (10-A)", changelog, StringComparison.Ordinal);
        Assert.Contains("(delete what users do not need)", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public void A_supplied_file_without_a_heading_is_refused_and_nothing_is_written()
    {
        var bad = Write("bad.md", "just some words\n");

        var (code, output) = RunScript("-ChangelogOnly", "-ChangelogFile", bad);

        Assert.NotEqual(0, code);
        Assert.Contains("does not start with a '## <version>' heading", output, StringComparison.Ordinal);
        Assert.DoesNotContain("just some words", File.ReadAllText(Path.Combine(releaseDir, "CHANGELOG.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_help_describes_the_changelog_and_its_parameters_and_the_commit_records_the_source()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "bin", "publish-release.ps1"));

        Assert.Contains("THE CHANGELOG", script, StringComparison.Ordinal);
        Assert.Contains(".PARAMETER ChangelogSince", script, StringComparison.Ordinal);
        Assert.Contains(".PARAMETER ChangelogFile", script, StringComparison.Ordinal);
        Assert.Contains("Changelog-source: $RefCommit", script, StringComparison.Ordinal);
    }

    // ---- fixture ---------------------------------------------------------------------------------------------------

    private string SavedDraft() => Path.Combine(scratchTemp, "ptk-release-1.1.0", "changelog-1.1.0.md");

    private string Write(string name, string text)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static string Card(string id, string title, string type, string status, string result = "Done.") =>
        $"# {id}: {title}\n\nCategory: {id[(id.IndexOf('-') + 1)..]}\n{(type.Length > 0 ? "Type: " + type + "\n" : "")}Status: {status}\n\n## Result\n{result}\n";

    private static string Props(string version) => $"<Project>\n  <PropertyGroup>\n    <Version>{version}</Version>\n  </PropertyGroup>\n</Project>\n";

    private void BuildFixture()
    {
        Git(repo, "init", "-q", "-b", "main");
        Commit(repo, "code/src/Directory.Build.props", Props("1.0.0"), null, "version 1.0.0");
        commits["base"] = Head(repo);
        Git(repo, "branch", "release");
        Git(repo, "worktree", "add", "-q", releaseDir, "release");
        File.WriteAllText(Path.Combine(releaseDir, "CHANGELOG.md"), "# ProjectToolkit changelog\n\n## 1.0.0 (2025-12-01)\n\n### New\n- The first release\n");
        Git(releaseDir, "add", "-A");
        Git(releaseDir, "commit", "-q", "-m", "ProjectToolkit 1.0.0", "-m", "From main at " + commits["base"][..8] + ".");
        Git(releaseDir, "tag", "-a", "v1.0.0", "-m", "ProjectToolkit 1.0.0");

        Commit(repo, "code/src/old.txt", "x", null, "code: old\n\nCard: 9-Z-old");
        Commit(repo, "jobs/done/9-Z-old.md", Card("9-Z", "Old one", "feature", "done"), null, "jobs: done 9-Z-old");
        Commit(repo, "code/src/Directory.Build.props", Props("1.1.0"), null, "version 1.1.0");
        Commit(repo, "code/src/a.txt", "x", null, "code: a\n\nCard: 10-A-feat");
        Commit(repo, "jobs/done/10-A-feat.md", Card("10-A", "Add the thing", "feature", "done"), null, "jobs: done 10-A-feat");
        Commit(repo, "bin/tool.ps1", "x", null, "tool: fix\n\nCard: 11-B-bug");
        Commit(repo, "jobs/done/11-B-bug.md", Card("11-B", "Fix the bug", "bug", "done"), null, "jobs: done 11-B-bug");
        commits["afterBug"] = Head(repo);
        Commit(repo, "docs/guide.md", "x", null, "docs: guide\n\nCard: 12-C-docs");
        Commit(repo, "jobs/done/12-C-docs.md", Card("12-C", "Docs only", "feature", "done"), null, "jobs: done 12-C-docs");
        Commit(repo, "jobs/done/13-D-nobump.md", Card("13-D", "No bump", "", "done", "Done. No version bump."), null, "jobs: done 13-D-nobump");
        Commit(repo, "code/src/e.txt", "x", null, "code: e\n\nCard: 14-E-untyped");
        Commit(repo, "jobs/done/14-E-untyped.md", Card("14-E", "Untyped one", "", "done"), null, "jobs: done 14-E-untyped");
        commits["head"] = Head(repo);
    }

    // Writes the file (with the given content), commits it with the message (path2 is an extra file touched alongside).
    private void Commit(string dir, string path, string content, string? path2, string message)
    {
        var full = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        Git(dir, "add", path);
        if (path2 is not null)
        {
            var full2 = Path.Combine(dir, path2.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full2)!);
            File.WriteAllText(full2, content, new UTF8Encoding(false));
            Git(dir, "add", path2);
        }

        Git(dir, "commit", "-q", "-m", message);
    }

    private static string Head(string dir) => Git(dir, "rev-parse", "HEAD").Trim();

    // The git.exe on PATH, as a full path (the fixture never starts git by name).
    private static string GitProgram() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim('"'), "git.exe"))
            .FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException("git.exe is not on PATH");

    private static string Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo(GitProgram())
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false" }.Concat(args))
        {
            start.ArgumentList.Add(a);
        }

        using var p = Process.Start(start)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(30_000), "git did not finish in 30 s");
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr.Result}");
        return stdout;
    }

    private (int Code, string Output) RunScript(params string[] args) => RunScript(args, stdin: "", editor: null);

    private (int Code, string Output) RunScript(string[] args, string stdin, string? editor)
    {
        var start = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        var all = new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepoRoot(), "bin", "publish-release.ps1"),
            "-Repo", repo, "-Ref", "main", "-ReleaseDir", releaseDir, "-NoFetch",
        }.Concat(args);
        foreach (var a in all)
        {
            start.ArgumentList.Add(a);
        }

        start.Environment["TEMP"] = scratchTemp;
        start.Environment["TMP"] = scratchTemp;
        if (editor is null)
        {
            start.Environment.Remove("EDITOR");
        }
        else
        {
            start.Environment["EDITOR"] = editor;
        }

        using var p = Process.Start(start)!;
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        var stderr = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "publish-release.ps1 did not finish in 60 s");
        return (p.ExitCode, output + stderr.Result);
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
