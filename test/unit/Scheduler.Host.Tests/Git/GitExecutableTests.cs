using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>Card 1043-B: one git executable, resolved once.</summary>
public class GitExecutableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ptk-gitexe-" + Guid.NewGuid().ToString("N"));

    public GitExecutableTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Configured_path_wins_when_it_runs()
    {
        var (path, how) = GitExecutable.Resolve(@"C:\x\git.exe", "", _ => true);
        Assert.Equal(@"C:\x\git.exe", path);
        Assert.Contains("configured", how);
    }

    [Fact]
    public void Configured_path_that_fails_falls_back_to_git()
    {
        Assert.Equal("git", GitExecutable.Resolve(@"C:\x\git.exe", "", _ => false).Path);
    }

    [Fact]
    public void Cmd_wrapper_on_path_gives_the_mingw64_binary()
    {
        Touch("Git", "cmd", "git.exe");
        var real = Touch("Git", "mingw64", "bin", "git.exe");
        Assert.Equal(real, GitExecutable.Resolve(null, Path.Combine(_root, "Git", "cmd"), _ => true).Path);
    }

    [Fact]
    public void Binary_that_fails_the_version_check_falls_back_to_git()
    {
        Touch("Git", "cmd", "git.exe");
        Touch("Git", "mingw64", "bin", "git.exe");
        Assert.Equal("git", GitExecutable.Resolve(null, Path.Combine(_root, "Git", "cmd"), _ => false).Path);
    }

    [Fact]
    public void No_git_on_path_or_a_git_outside_cmd_stays_git()
    {
        Touch("Other", "bin", "git.exe");
        Assert.Equal("git", GitExecutable.Resolve(null, Path.Combine(_root, "Other", "bin"), _ => true).Path);
        Assert.Equal("git", GitExecutable.Resolve(null, "", _ => true).Path);
    }

    [Fact]
    public void Setting_is_read_from_a_json_file()
    {
        var file = Path.Combine(_root, "pool.json");
        File.WriteAllText(file, "{ \"git.exe\": \"C:\\\\g\\\\git.exe\" }");
        Assert.Equal(@"C:\g\git.exe", GitExecutable.SettingIn(file));
        Assert.Null(GitExecutable.SettingIn(Path.Combine(_root, "none.json")));
    }

    [Fact]
    public void Choose_takes_the_pool_file_setting_alone()
    {
        var c = GitExecutable.Choose(@"C:\p\git.exe", "pool.json", [("a", null)]);
        Assert.Equal(@"C:\p\git.exe", c.Value);
        Assert.Contains("pool.json", c.Source);
        Assert.Empty(c.Notes);
    }

    [Fact]
    public void Choose_takes_a_scheduler_json_setting_alone()
    {
        var c = GitExecutable.Choose(null, "pool.json", [("a", null), ("b", @"C:\b\git.exe")]);
        Assert.Equal(@"C:\b\git.exe", c.Value);
        Assert.Equal("b's scheduler.json", c.Source);
        Assert.Empty(c.Notes);
    }

    [Fact]
    public void Choose_prefers_the_pool_file_when_both_name_one()
    {
        var c = GitExecutable.Choose(@"C:\p\git.exe", "pool.json", [("a", @"C:\a\git.exe")]);
        Assert.Equal(@"C:\p\git.exe", c.Value);
        Assert.Contains("pool.json", c.Source);
        Assert.Contains(@"a's scheduler.json names C:\a\git.exe", Assert.Single(c.Notes));
    }

    [Fact]
    public void Choose_takes_the_first_project_when_projects_disagree()
    {
        var c = GitExecutable.Choose(" ", "pool.json", [("a", @"C:\a\git.exe"), ("b", @"C:\b\git.exe"), ("c", @"c:\A\git.exe")]);
        Assert.Equal(@"C:\a\git.exe", c.Value);
        Assert.Equal("a's scheduler.json", c.Source);
        Assert.Contains("b's scheduler.json names", Assert.Single(c.Notes)); // c agrees (case aside)
    }

    [Fact]
    public void Choose_with_nothing_named_takes_nothing()
    {
        var c = GitExecutable.Choose(null, "pool.json", [("a", null)]);
        Assert.Null(c.Value);
        Assert.Empty(c.Notes);
    }

    [Fact]
    public void The_log_lines_say_which_setting_was_taken_and_from_where()
    {
        // Card 1096-B: no GitExecutable.Configure here. The choice is process-wide and pool tests running in
        // parallel set it too (PoolComposition), which once replaced the notes between Configure and DescribeLines.
        var real = Touch("g", "git.exe");
        var choice = GitExecutable.Choose(null, "pool.json", [("a", real), ("b", @"C:\b\git.exe")]);
        var (path, how) = GitExecutable.Resolve(choice.Value, "", _ => true);
        var lines = GitExecutable.LinesFor(path, how, choice.Source, choice.Notes);
        Assert.Equal(2, lines.Count);
        Assert.Contains(real, lines[0]);
        Assert.Contains("taken from a's scheduler.json", lines[0]);
        Assert.Contains("b's scheduler.json names", lines[1]);
    }

    [Fact]
    public void Setting_is_read_from_json_text()
    {
        Assert.Equal(@"C:\g\git.exe", GitExecutable.SettingOf("{ \"git.exe\": \"C:\\\\g\\\\git.exe\" }"));
        Assert.Null(GitExecutable.SettingOf("{ }"));
        Assert.Null(GitExecutable.SettingOf("not json"));
    }

    [Fact]
    public void Nothing_in_the_scheduler_or_its_tests_starts_git_by_name()
    {
        var repo = AppContext.BaseDirectory;
        while (repo is not null && !Directory.Exists(Path.Combine(repo, "code", "src")))
        {
            repo = Path.GetDirectoryName(repo);
        }

        Assert.NotNull(repo);
        var hits = new[] { "code", "test" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(repo, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "perf" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllText(f).Contains("ProcessStartInfo(\"git\"", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(hits);
    }
}
