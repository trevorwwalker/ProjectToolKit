using System.Diagnostics;

namespace Scheduler.Host.Git;

/// <summary>
/// Card 1043-B: the one git program the scheduler starts, resolved once. <c>git</c> on PATH in Git for Windows is
/// <c>&lt;root&gt;\cmd\git.exe</c>, a wrapper that starts <c>&lt;root&gt;\mingw64\bin\git.exe</c>: two process starts per
/// command. The configured <c>git.exe</c> wins; else the real binary beside the wrapper; else "git" on PATH. A
/// choice that fails <c>--version</c> falls back to "git".
/// </summary>
public static class GitExecutable
{
    private static readonly object Gate = new();
    private static string? _configured;
    private static string? _resolved;
    private static string _how = "";
    private static string _source = "";
    private static IReadOnlyList<string> _notes = [];

    /// <summary>The git program to start; resolved on first use, then fixed until <see cref="Configure"/> is called.</summary>
    public static string Path
    {
        get
        {
            lock (Gate)
            {
                if (_resolved is null)
                {
                    (_resolved, _how) = Resolve(_configured, Environment.GetEnvironmentVariable("PATH"), RunsVersion);
                }

                return _resolved;
            }
        }
    }

    /// <summary>One line for the startup block: the path chosen and why.</summary>
    public static string Describe()
    {
        var p = Path;
        lock (Gate)
        {
            return LinesFor(p, _how, _source, [])[0];
        }
    }

    /// <summary>Card 1081-B: <see cref="Describe"/> and a line for each other setting that was not taken.</summary>
    public static IReadOnlyList<string> DescribeLines()
    {
        var p = Path;
        lock (Gate)
        {
            return LinesFor(p, _how, _source, _notes);
        }
    }

    /// <summary>
    /// Card 1096-B: the startup lines for a resolved path, how it was found, where the setting came from and the
    /// settings not taken. Pure, so tests need not touch the process-wide choice other tests also set.
    /// </summary>
    public static IReadOnlyList<string> LinesFor(string path, string how, string source, IReadOnlyList<string> notes) =>
        [$"startup: git: {path} ({how}{(source.Length > 0 ? "; git.exe taken from " + source : "")})", .. notes];

    /// <summary>Set the configured <c>git.exe</c> (null or empty for none) and resolve again on next use.</summary>
    public static void Configure(string? configured) => Configure(configured, "", []);

    /// <summary>Card 1081-B: as above, with where the setting was read from and the settings that were not taken.</summary>
    public static void Configure(string? configured, string source, IReadOnlyList<string> notes)
    {
        lock (Gate)
        {
            _configured = string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
            _source = _configured is null ? "" : source;
            _notes = notes;
            _resolved = null;
        }
    }

    /// <summary>Card 1081-B: apply <see cref="Choose"/>'s answer.</summary>
    public static void Configure(GitChoice choice) => Configure(choice.Value, choice.Source, choice.Notes);

    /// <summary>
    /// Card 1081-B (MM-2's order): the pool file's <c>git.exe</c>, else the first project's <c>scheduler.json</c> that names one.
    /// One git per process, so a setting that differs from the one taken is listed in <see cref="GitChoice.Notes"/>.
    /// </summary>
    public static GitChoice Choose(string? poolValue, string poolName, IEnumerable<(string Project, string? Value)> projects)
    {
        var named = projects.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => (p.Project, Value: p.Value!.Trim())).ToList();
        string? value;
        string source;
        if (!string.IsNullOrWhiteSpace(poolValue))
        {
            value = poolValue.Trim();
            source = $"the pool file {poolName}";
        }
        else if (named.Count > 0)
        {
            value = named[0].Value;
            source = $"{named[0].Project}'s scheduler.json";
        }
        else
        {
            return new GitChoice(null, "", []);
        }

        var notes = named.Where(p => !string.Equals(p.Value, value, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"startup: git: {p.Project}'s scheduler.json names {p.Value}, not used: one git.exe for the whole process")
            .ToList();
        return new GitChoice(value, source, notes);
    }

    /// <summary>The <c>git.exe</c> setting of a JSON settings file (the pool file, scheduler.json); null when absent or unreadable.</summary>
    public static string? SettingIn(string jsonFile)
    {
        try
        {
            return SettingOf(File.ReadAllText(jsonFile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The <c>git.exe</c> setting of a JSON settings text; null when absent or not JSON.</summary>
    public static string? SettingOf(string jsonText)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonText, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("git.exe", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                    ? v.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Give <paramref name="psi"/> the environment the <c>cmd\git.exe</c> wrapper would have: when the resolved git is
    /// <c>&lt;root&gt;\mingw64\bin\git.exe</c>, <c>&lt;root&gt;\mingw64\bin</c> and <c>&lt;root&gt;\usr\bin</c> go first on PATH (hooks need
    /// sh, cat, tr), and MSYSTEM is set when absent. No change for any other git.
    /// </summary>
    public static ProcessStartInfo Apply(ProcessStartInfo psi)
    {
        var exe = Path;
        if (!System.IO.Path.IsPathRooted(exe) || !string.Equals(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(exe)), "bin", StringComparison.OrdinalIgnoreCase))
        {
            return psi;
        }

        var bin = System.IO.Path.GetDirectoryName(exe)!;
        var mingw = System.IO.Path.GetDirectoryName(bin);
        var root = mingw is null ? null : System.IO.Path.GetDirectoryName(mingw);
        if (root is null || !string.Equals(System.IO.Path.GetFileName(mingw), "mingw64", StringComparison.OrdinalIgnoreCase))
        {
            return psi;
        }

        var key = psi.Environment.Keys.FirstOrDefault(k => string.Equals(k, "PATH", StringComparison.OrdinalIgnoreCase)) ?? "PATH";
        psi.Environment[key] = bin + System.IO.Path.PathSeparator + System.IO.Path.Combine(root, "usr", "bin") + System.IO.Path.PathSeparator + (psi.Environment.TryGetValue(key, out var old) ? old : "");
        if (!psi.Environment.ContainsKey("MSYSTEM"))
        {
            psi.Environment["MSYSTEM"] = "MINGW64";
        }

        return psi;
    }

    /// <summary>The choice, with its reason; <paramref name="works"/> is the <c>--version</c> self-check.</summary>
    public static (string Path, string How) Resolve(string? configured, string? pathVar, Func<string, bool> works)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return works(configured) ? (configured, "configured git.exe") : ("git", $"configured {configured} failed --version; git on PATH");
        }

        foreach (var dir in (pathVar ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string exe;
            try
            {
                exe = System.IO.Path.Combine(dir.Trim().Trim('"'), "git.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!File.Exists(exe))
            {
                continue;
            }

            var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(exe));
            if (parent is not null && string.Equals(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(exe)), "cmd", StringComparison.OrdinalIgnoreCase))
            {
                var real = System.IO.Path.Combine(parent, "mingw64", "bin", "git.exe");
                if (File.Exists(real))
                {
                    return works(real) ? (real, "found beside the cmd wrapper") : ("git", $"{real} failed --version; git on PATH");
                }
            }

            break; // the first git on PATH is the one that would run
        }

        return ("git", "git on PATH");
    }

    private static bool RunsVersion(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15000))
            {
                p.Kill(true);
                return false;
            }

            return p.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>Card 1081-B: the <c>git.exe</c> taken, where from, and the settings not taken (a line each, for the log).</summary>
public sealed record GitChoice(string? Value, string Source, IReadOnlyList<string> Notes);
