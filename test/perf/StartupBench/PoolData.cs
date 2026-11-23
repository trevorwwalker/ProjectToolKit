using System.Diagnostics;
using System.Text;

namespace StartupBench;

/// <summary>
/// Writes a pool to disk (1032-B): a pool file and projects with real git repositories (and a bare "origin"
/// each, as a live project has), generated cards in queue/, review/ and done/. Every name and folder is new,
/// under one temp root, so the live pool, its pool.json, its ports and its logs are never touched.
/// </summary>
public sealed class PoolData
{
    private PoolData(string root, string poolFile, IReadOnlyList<string> names)
    {
        Root = root;
        PoolFile = poolFile;
        ProjectNames = names;
    }

    public string Root { get; }

    public string PoolFile { get; }

    public IReadOnlyList<string> ProjectNames { get; }

    /// <summary>The pool: <paramref name="projects"/> projects of <paramref name="cards"/> cards and one of <paramref name="bigCards"/>.</summary>
    public static PoolData Create(string root, int projects, int cards, int bigCards, int port)
    {
        Directory.CreateDirectory(root);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var counts = Enumerable.Repeat(cards, projects).Append(bigCards).ToList();
        // Project names are mutex names, machine-wide: unique to this run.
        var names = Enumerable.Range(1, counts.Count).Select(i => $"ptkperf-{tag}-{i}").ToList();
        Parallel.For(0, counts.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            i => MakeProject(root, names[i], Path.Combine(root, names[i]), counts[i]));
        var entries = names.Select(n => $"{{\"name\":\"{n}\",\"repo\":\"{Json(Path.Combine(root, n))}\",\"weight\":50}}").ToList();

        var poolFile = Path.Combine(root, "pool.json");
        File.WriteAllText(poolFile,
            $"{{\"projects\":[{string.Join(",", entries)}],\"port\":{port},\"listen\":\"local\",\"logDir\":\"{Json(Path.Combine(root, "poollog"))}\","
            + "\"pool\":{\"maxWorkers\":2,\"types\":{\"Bench\":{\"max\":2}}}}");
        return new PoolData(root, poolFile, names);
    }

    /// <summary>Point the pool file at another port (each start has a free one of its own).</summary>
    public void SetPort(int port)
    {
        var text = File.ReadAllText(PoolFile);
        var at = text.IndexOf("\"port\":", StringComparison.Ordinal) + 7;
        var end = text.IndexOf(',', at);
        File.WriteAllText(PoolFile, text[..at] + port + text[end..]);
    }

    /// <summary>Delete the pool's folder; git leaves its object files read-only.</summary>
    public static void Delete(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, true);
    }

    private static string Json(string path) => path.Replace("\\", "\\\\");

    private static void MakeProject(string root, string name, string repo, int cards)
    {
        var jobs = Path.Combine(repo, "jobs");
        foreach (var folder in new[] { "queue", "active", "review", "done", "plans", "requests" })
        {
            Directory.CreateDirectory(Path.Combine(jobs, folder));
        }

        File.WriteAllText(Path.Combine(jobs, "categories.json"),
            "{\"lanes\":{\"A\":{\"name\":\"one\",\"purpose\":\"bench\",\"owns\":[\"src/a/\"],\"except\":[]},"
            + "\"B\":{\"name\":\"two\",\"purpose\":\"bench\",\"owns\":[\"src/b/\"],\"except\":[]}}}");
        File.WriteAllText(Path.Combine(jobs, "scheduler.json"),
            $"{{\"project\":\"{name}\",\"slots\":2,\"jobsDir\":\"jobs\",\"remote\":\"origin\",\"branch\":\"main\","
            + $"\"workspaceRoot\":\"{Json(Path.Combine(root, name + "-ws"))}\",\"logDir\":\"{Json(Path.Combine(root, name + "-log"))}\","
            + "\"worker\":{\"cli\":\"none\",\"args\":[\"{prompt}\"]},\"health\":{\"command\":\"\"},"
            + "\"limits\":{\"pollSeconds\":15,\"maxRetries\":2,\"stallMinutes\":30,\"warnMinutes\":20,\"stopGraceMinutes\":20,\"maxMinutes\":240}}");
        File.WriteAllText(Path.Combine(jobs, "project.md"), "# bench\n\n## Gates\n\n- none\n");

        // A tenth of the cards waiting, 3% in review, the rest done: a project's history is mostly done/.
        for (var n = 1; n <= cards; n++)
        {
            var (folder, status) = n % 10 == 1 ? ("queue", "ready") : n % 33 == 2 ? ("review", "review: check it") : ("done", "done");
            var letter = n % 2 == 0 ? 'A' : 'B';
            File.WriteAllText(Path.Combine(jobs, folder, $"{n}-{letter}-card-number-{n}.md"), Card(n, letter, status));
        }

        Git(repo, "init", "-q", "-b", "main");
        Git(repo, "add", "-A");
        Git(repo, "-c", "user.name=bench", "-c", "user.email=bench@example.invalid", "commit", "-q", "-m", "bench pool");
        var origin = Path.Combine(root, name + "-origin.git");
        Git(root, "clone", "-q", "--bare", repo, origin);

        // What "git remote add origin", "git fetch" and "set-upstream-to" leave, written directly: no process each.
        var gitDir = Path.Combine(repo, ".git");
        var tip = File.ReadAllText(Path.Combine(gitDir, "refs", "heads", "main")).Trim();
        Directory.CreateDirectory(Path.Combine(gitDir, "refs", "remotes", "origin"));
        File.WriteAllText(Path.Combine(gitDir, "refs", "remotes", "origin", "main"), tip + "\n");
        File.AppendAllText(Path.Combine(gitDir, "config"),
            "[remote \"origin\"]\n\turl = " + origin.Replace('\\', '/') + "\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n"
            + "[branch \"main\"]\n\tremote = origin\n\tmerge = refs/heads/main\n");
    }

    private static string Card(int n, char letter, string status)
    {
        var sb = new StringBuilder();
        sb.Append($"# {n}-{letter}: Generated card number {n}\n\nCategory: {letter}\nType: feature\nPlan: none\nStatus: {status}\n");
        sb.Append("Priority: normal\nDepends on: none\nScope: src/" + char.ToLowerInvariant(letter) + "/\nLocks: none\nTime box: 60 min\n\n## Task\n\n");
        for (var i = 0; i < 6; i++)
        {
            sb.Append($"Line {i} of the task of card {n}: a few sentences of plain prose, as a real card has, so reading it costs what reading a real one costs.\n");
        }

        sb.Append("\n## Done when\n\n- It works.\n\n## Questions\n\n## Result\n\n");
        for (var i = 0; i < 10; i++)
        {
            sb.Append($"2026-11-0{1 + i % 9} 10:{i:00} journal line {i} of card {n}: what was found, what was changed, what is next.\n");
        }

        return sb.ToString();
    }

    private static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} in {dir} failed: {err.Result}");
        }
    }
}
