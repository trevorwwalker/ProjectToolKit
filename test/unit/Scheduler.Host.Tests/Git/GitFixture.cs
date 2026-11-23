using System.Diagnostics;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Git;

/// <summary>A temporary bare remote, a clone that pushes to it (Seed) and the clone the scheduler reads (Repo). Never this repository.</summary>
internal sealed class GitFixture : IDisposable
{
    private readonly TempDir _temp = new();

    public string Bare { get; }
    public string Seed { get; }
    public string Repo { get; }

    public GitFixture()
    {
        Bare = Path.Combine(_temp.Path, "remote.git");
        Seed = Path.Combine(_temp.Path, "seed");
        Repo = Path.Combine(_temp.Path, "repo");
        // 1026-B: the three empty repositories are copied from ones made once per test run, and the remote is
        // written into each clone's config as `git remote add origin <Bare>` writes it: no git process per
        // fixture. In a full run every git (and cmd) the tests start queues for the one lock .NET holds around
        // starting a process on Windows, so five fewer per fixture is time every other test gets back.
        CopyTree(Path.Combine(Template.Value, "remote.git"), Bare);
        CopyTree(Path.Combine(Template.Value, "seed"), Seed);
        CopyTree(Path.Combine(Template.Value, "repo"), Repo);
        var origin = $"[remote \"origin\"]\n\turl = {Bare.Replace("\\", "\\\\", StringComparison.Ordinal)}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n";
        File.AppendAllText(Path.Combine(Seed, ".git", "config"), origin);
        File.AppendAllText(Path.Combine(Repo, ".git", "config"), origin);
        // 1068-B: the remote borrows the Seed clone's objects, so Push can set the remote's main by writing the ref
        // (what `git push origin HEAD:main` ends in) instead of starting a push: no git-receive-pack, no sh.
        var infoDir = Path.Combine(Bare, "objects", "info");
        Directory.CreateDirectory(infoDir);
        File.WriteAllText(Path.Combine(infoDir, "alternates"), Path.Combine(Seed, ".git", "objects").Replace('\\', '/') + "\n");
    }

    // The empty bare remote and two empty clones (no remote yet) every fixture starts from, made once per run.
    private static readonly Lazy<string> Template = new(() =>
    {
        var root = Scheduler.TestSupport.TestTmp.NewPath("tpl");
        Directory.CreateDirectory(Path.Combine(root, "remote.git"));
        Directory.CreateDirectory(Path.Combine(root, "seed"));
        Directory.CreateDirectory(Path.Combine(root, "repo"));
        Git(Path.Combine(root, "remote.git"), "init", "-q", "--bare", "-b", "main");
        Git(Path.Combine(root, "seed"), "init", "-q", "-b", "main");
        Git(Path.Combine(root, "repo"), "init", "-q", "-b", "main");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // left in out	est-tmp like any other temp folder
            }
        };
        return root;
    });

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    public static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo(Scheduler.Host.Git.GitExecutable.Path) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false }; _ = Scheduler.Host.Git.GitExecutable.Apply(psi);
        // safe.bareRepository=all: this fixture runs git inside its own bare remote (rev-parse, show),
        // which a git configured for explicit bare repositories refuses without being told otherwise.
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "-c", "safe.bareRepository=all" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        // No input, ever: a git that reads stdin (hash-object --stdin) otherwise reads the terminal the tests were
        // started from and waits for the keyboard (it only ended at once where stdin was not a console).
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {o}{err.Result}");
        return o.Trim();
    }

    /// <summary>Write files into Seed (path to text; null removes), commit and push to the remote's main.</summary>
    public void Push(string message, IDictionary<string, string?> files)
    {
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(Seed, path);
            if (text is null)
            {
                File.Delete(full);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        Git(Seed, "add", "-A");
        Git(Seed, "commit", "-q", "-m", message);
        PublishMain();
    }

    // Same end state as `git push -q origin HEAD:main` in the Seed clone: the remote's main and the Seed's
    // remote-tracking ref both name Seed's HEAD. HEAD is read from the loose ref; git is asked only when it is packed.
    private void PublishMain()
    {
        var headRef = Path.Combine(Seed, ".git", "refs", "heads", "main");
        var sha = File.Exists(headRef) ? File.ReadAllText(headRef).Trim() : Git(Seed, "rev-parse", "HEAD");
        WriteRef(Path.Combine(Bare, "refs", "heads", "main"), sha);
        WriteRef(Path.Combine(Seed, ".git", "refs", "remotes", "origin", "main"), sha);
    }

    private static void WriteRef(string path, string sha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sha + "\n");
    }

    public MainRepository Main(GitRunner? runner = null) => new(runner ?? new GitRunner(), Repo, "origin", "main");

    public void Dispose() => _temp.Dispose();
}

/// <summary>
/// An <see cref="ISchedulerLog"/> that keeps its lines in memory. The lock is for the tests that
/// run the program loop on its own thread (934-B) and read the log from the test thread, so
/// <see cref="Lines"/> hands out a snapshot rather than the live list.
/// </summary>
internal sealed class ListLog : ISchedulerLog
{
    private readonly List<string> _lines = [];

    public List<string> Lines
    {
        get { lock (_lines) return [.. _lines]; }
    }

    public void Write(string line)
    {
        lock (_lines) _lines.Add(line);
    }
}

/// <summary>An <see cref="IJobsCommitter"/> that removes files on the remote through the Seed clone, like the answers worktree does.</summary>
internal sealed class SeedCommitter(GitFixture fx) : IJobsCommitter
{
    public List<string> Messages { get; } = [];

    public CommitResult Apply(JobsChange change)
    {
        Messages.Add(change.Message);
        GitFixture.Git(fx.Seed, "pull", "-q", "--rebase", "origin", "main");
        foreach (var r in change.Removals)
        {
            GitFixture.Git(fx.Seed, "rm", "-q", r);
        }

        GitFixture.Git(fx.Seed, "commit", "-q", "-m", change.Message);
        GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:main");
        return new CommitResult { Ok = true };
    }
}
