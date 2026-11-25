// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Categories;
using Scheduler.Core.Launch;
using Scheduler.Core.Locks;
using Scheduler.Host.Git;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Program;

/// <summary>
/// Dynamic locks, the push check (design <c>dynamic-locks.md</c>, DL-15 to DL-18; card 1021-B):
/// <c>scheduler-next check-push [&lt;remote&gt; [&lt;url&gt;]]</c>, run by the scheduler's <c>pre-push</c> hook
/// with git's ref lines (<c>&lt;local ref&gt; &lt;local sha&gt; &lt;remote ref&gt; &lt;remote sha&gt;</c>) on its input.
/// For each ref that is the main branch it lists the paths changed in <c>&lt;remote sha&gt;..&lt;local sha&gt;</c>
/// and asks <see cref="PushCheck"/> against the card's held set in the lock table file; any refused path
/// gives a non-zero exit, which refuses the push. Other refs (<c>wip/&lt;card&gt;</c>) are never refused.
/// It reads git and the table file only, changes nothing and never starts a scheduler.
/// </summary>
public static class CheckPushCommand
{
    /// <summary>The subcommand's name.</summary>
    public const string Switch = "check-push";

    /// <summary>The exit code of a refused push.</summary>
    public const int Refused = 1;

    private const string Zero = "0000000000000000000000000000000000000000";

    /// <summary>
    /// Runs the check in <paramref name="workingDirectory"/> (the worktree git runs the hook in).
    /// <paramref name="args"/> are those after <c>check-push</c>; <paramref name="input"/> is git's ref lines.
    /// </summary>
    public static int Run(
        IReadOnlyList<string> args,
        Func<string, string?> env,
        TextReader input,
        TextWriter output,
        string workingDirectory,
        GitRunner? git = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var card = (env(WorkerLocks.CardVariable) ?? "").Trim();
        if (card.Length == 0)
        {
            return 0;   // not a worker the scheduler started: nothing to check
        }

        git ??= new GitRunner(timeout: TimeSpan.FromSeconds(60));
        var remote = args.Count > 0 && args[0].Length > 0 ? args[0] : "origin";
        var main = MainBranch(git, workingDirectory, remote);
        var logDir = LockCommand.LogDir(env);
        var row = logDir.Length > 0 && Directory.Exists(logDir) ? LockCommand.Row(logDir, card) : null;
        var exe = env(WorkerLocks.ExeVariable) is { Length: > 0 } e ? e : Environment.ProcessPath ?? "scheduler-next.exe";
        var refused = false;
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !IsMain(parts[2], main))
            {
                continue;   // wip/<card> and every other ref: never refused (DL-18)
            }

            var (local, remoteSha) = (parts[1], parts[3]);
            if (local == Zero)
            {
                output.WriteLine($"{card} may not delete {main}");
                refused = true;
                continue;
            }

            var from = remoteSha == Zero ? Resolve(git, workingDirectory, $"refs/remotes/{remote}/{main}") : remoteSha;
            if (from is null || !git.Run(workingDirectory, "cat-file", "-e", from + "^{commit}").Ok)
            {
                output.WriteLine($"{card}: {main} has moved on since the last pull; pull --rebase origin {main} and push again");
                refused = true;
                continue;
            }

            var result = PushCheck.Check(new PushCheckRequest
            {
                Card = card,
                Ref = parts[2],
                MainBranch = main,
                Paths = Paths(git, workingDirectory, from, local),
                Locks = row,
                AddOnly = AddOnly(git, workingDirectory, local),
                MainVersion = Version(git, workingDirectory, from),
                PushedVersion = Bumped(git, workingDirectory, from, local),
                Exe = exe,
            });
            foreach (var w in result.Warnings)
            {
                output.WriteLine($"warning: {w}");
            }

            foreach (var l in result.Lines)
            {
                output.WriteLine(l);
            }

            refused |= !result.Ok;
        }

        if (refused)
        {
            RecordRefusal(logDir, card);   // DL-26: the page counts it
            output.WriteLine($"check-push: push refused; push your work to wip/{card} instead if you cannot get the lock");
        }

        return refused ? Refused : 0;
    }

    /// <summary>DL-26: appends a line to the refused-pushes file of the table that holds the card (or the only table).</summary>
    private static void RecordRefusal(string logDir, string card)
    {
        var dir = logDir.Length > 0 ? System.IO.Path.Combine(logDir, LockTableText.FolderName) : "";
        if (dir.Length == 0 || !Directory.Exists(dir))
        {
            return;
        }

        var tables = Directory.EnumerateFiles(dir, "*.json").ToList();
        var own = tables.FirstOrDefault(f => Holds(f, card)) ?? (tables.Count == 1 ? tables[0] : null);
        if (own is not null)
        {
            LockTableFile.AppendPushRefused(own, DateTimeOffset.Now);
        }
    }

    private static bool Holds(string tableFile, string card)
    {
        try
        {
            return LockTableText.Read(File.ReadAllText(tableFile), "").Table.Find(card) is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The paths <c>from..to</c> changes, with whether each is new and its line counts.</summary>
    internal static IReadOnlyList<PushCheckPath> Paths(GitRunner git, string dir, string from, string to)
    {
        var status = git.Run(dir, "-c", "core.quotePath=false", "diff", "--no-renames", "--name-status", "-z", from, to);
        var counts = git.Run(dir, "-c", "core.quotePath=false", "diff", "--no-renames", "--numstat", "-z", from, to);
        var added = new Dictionary<string, (int Added, int Deleted)>(StringComparer.Ordinal);
        foreach (var entry in counts.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = entry.Split('\t', 3);
            if (f.Length == 3)
            {
                added[f[2]] = (Count(f[0]), Count(f[1]));
            }
        }

        var paths = new List<PushCheckPath>();
        var s = status.Stdout.Split('\0');
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var path = s[i + 1];
            if (path.Length == 0)
            {
                continue;
            }

            var (a, d) = added.GetValueOrDefault(path);
            paths.Add(new PushCheckPath(path, New: s[i].StartsWith('A'), Added: a, Deleted: d));
        }

        return paths;
    }

    // The version file is code/src/Directory.Build.props (PushCheckRequest's default).
    private static readonly string VersionFile = new PushCheckRequest { Card = "" }.VersionFile;

    /// <summary>The <c>&lt;Version&gt;</c> of the version file at <paramref name="commit"/>, or null.</summary>
    internal static string? Version(GitRunner git, string dir, string commit)
    {
        var r = git.Run(dir, "show", $"{commit}:{VersionFile}");
        var m = r.Ok ? Regex.Match(r.Stdout, @"<Version>\s*([^<\s]+)\s*</Version>", RegexOptions.None, TimeSpan.FromSeconds(1)) : null;
        return m is { Success: true } ? m.Groups[1].Value : null;
    }

    // The pushed version when the range changes the <Version> line, else null (DL-17).
    private static string? Bumped(GitRunner git, string dir, string from, string to)
    {
        var pushed = Version(git, dir, to);
        return pushed is not null && pushed != Version(git, dir, from) ? pushed : null;
    }

    // categories.json's shared add-only paths, as the pushed commit has them (DL-15 a).
    private static IReadOnlyList<string> AddOnly(GitRunner git, string dir, string commit)
    {
        var r = git.Run(dir, "show", $"{commit}:jobs/categories.json");
        try
        {
            return r.Ok ? CategoriesEdit.Parse(r.Stdout)?.AddOnly ?? [] : [];
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    // The remote's default branch (refs/remotes/<remote>/HEAD), else main.
    private static string MainBranch(GitRunner git, string dir, string remote)
    {
        var r = git.Run(dir, "symbolic-ref", "--quiet", $"refs/remotes/{remote}/HEAD");
        var prefix = $"refs/remotes/{remote}/";
        var name = r.Ok ? r.Stdout.Trim() : "";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length ? name[prefix.Length..] : "main";
    }

    private static string? Resolve(GitRunner git, string dir, string rev)
    {
        var r = git.Run(dir, "rev-parse", "--verify", "--quiet", rev);
        return r.Ok && r.Stdout.Trim().Length > 0 ? r.Stdout.Trim() : null;
    }

    private static bool IsMain(string remoteRef, string main) =>
        remoteRef == main || remoteRef == "refs/heads/" + main;

    private static int Count(string n) => int.TryParse(n, out var v) ? v : 0;
}
