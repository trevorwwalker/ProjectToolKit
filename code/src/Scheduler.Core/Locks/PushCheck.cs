// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Locks;

/// <summary>How DL-15 sees a changed path, before the held set is asked.</summary>
internal enum PushPathKind
{
    /// <summary>Under no exemption: allowed only when the held set covers it.</summary>
    Locked,

    /// <summary>A shared add-only file (DL-15 a).</summary>
    AddOnly,

    /// <summary>The card's own card file, or a new file under <c>jobs/requests/</c> (DL-15 b, c).</summary>
    Exempt,

    /// <summary>A protected path under <c>jobs/</c> (<see cref="ProtectedJobs"/>): the scheduler's or the planner's, refused even when held.</summary>
    Jobs,
}

/// <summary>
/// The push check (design <c>dynamic-locks.md</c>, DL-15 to DL-18), pure: the paths a push to main
/// changes, their line counts and the card's held set, to the paths refused and the warnings. Host's
/// <c>check-push</c> subcommand gathers the input from git and the lock table file, and prints the lines.
/// </summary>
public static class PushCheck
{
    /// <summary>The words a version not above main's is refused with (DL-17).</summary>
    public const string BumpAgain = "bump again from main's version";

    /// <summary>DL-15 to DL-18 on one pushed ref.</summary>
    public static PushCheckResult Check(PushCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsMain(request.Ref, request.MainBranch))
        {
            return new PushCheckResult();
        }

        var refused = new List<string>();
        var lines = new List<string>();
        var warnings = new List<string>();
        var bumped = request.PushedVersion is not null;
        var addOnly = request.AddOnly.Concat(request.Locks?.AddOnly ?? []).ToList();
        foreach (var changed in request.Paths)
        {
            var path = Normal(changed.Path);
            switch (Kind(request.Card, path, changed.New, addOnly))
            {
                case PushPathKind.AddOnly:
                    // DL-16: the bumped <Version> line is the one deletion an add-only file may have.
                    var allowed = bumped && Same(path, request.VersionFile) ? 1 : 0;
                    if (changed.Deleted > allowed)
                    {
                        warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{request.Card} deleted {changed.Deleted} line(s) of add-only {path}"));
                    }

                    break;
                case PushPathKind.Exempt:
                    break;
                case PushPathKind.Jobs:
                    refused.Add(path);
                    lines.Add($"{request.Card} may not change {path}: {ProtectedJobs.Rule}");
                    break;
                default:
                    if (request.Locks is not { Holds: true } held || !held.Covers(path))
                    {
                        refused.Add(path);
                        lines.Add($"{request.Card} does not hold {path}; ask: {request.Exe} lock {path} --why \"<one line>\"");
                    }

                    break;
            }
        }

        if (bumped && !Above(request.PushedVersion!, request.MainVersion))
        {
            lines.Add($"{request.Card}: version {request.PushedVersion} is not above main's {request.MainVersion}: {BumpAgain}");
        }

        return new PushCheckResult { Refused = refused, Lines = lines, Warnings = warnings };
    }

    /// <summary>DL-15: how a changed path stands before the held set is asked.</summary>
    internal static PushPathKind Kind(string card, string path, bool isNew, IEnumerable<string> addOnly)
    {
        path = Normal(path);
        if (addOnly.Any(a => PathGlob.Covers(Normal(a), path)))
        {
            return PushPathKind.AddOnly;
        }

        if (!path.StartsWith("jobs/", StringComparison.OrdinalIgnoreCase))
        {
            return PushPathKind.Locked;
        }

        var parts = path.Split('/');
        if (parts.Length == 3 && ProtectedJobs.IsCardFolder(parts[1]) && Same(parts[2], card + ".md"))
        {
            return PushPathKind.Exempt;
        }

        if (ProtectedJobs.IsProtected(path))
        {
            return PushPathKind.Jobs;
        }

        if (isNew && parts.Length >= 3 && Same(parts[1], "requests"))
        {
            return PushPathKind.Exempt;
        }

        // DL-15 (developer, 2026-11-21): every other path under jobs/ is an ordinary, lockable path.
        return PushPathKind.Locked;
    }

    /// <summary>A repository path with slashes, no leading <c>./</c> or <c>/</c>.</summary>
    internal static string Normal(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p.TrimStart('/');
    }

    private static bool Same(string a, string b) => Text.Same(Normal(a), Normal(b));

    private static bool IsMain(string target, string main)
    {
        var t = target.Trim();
        const string heads = "refs/heads/";
        if (t.StartsWith(heads, StringComparison.Ordinal))
        {
            t = t[heads.Length..];
        }

        return string.Equals(t, main, StringComparison.Ordinal);
    }

    // DL-17: the pushed version must be greater than main's; one that cannot be read is refused,
    // and with main's unknown there is nothing to compare against.
    private static bool Above(string pushed, string? main)
    {
        if (!Version.TryParse(pushed.Trim(), out var p))
        {
            return false;
        }

        return main is null || !Version.TryParse(main.Trim(), out var m) || p > m;
    }
}
