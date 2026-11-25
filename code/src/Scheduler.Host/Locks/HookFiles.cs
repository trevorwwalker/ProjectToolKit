// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Locks;

/// <summary>
/// Dynamic locks, enforcement (design <c>dynamic-locks.md</c>, DL-18; card 1021-B): the scheduler's own git
/// hooks, <c>&lt;log&gt;\hooks\commit-msg</c> and <c>pre-push</c>, written at start when locks are on. A worker's
/// git runs them through <c>core.hooksPath</c> in its environment (<c>WorkerLocks</c>); nothing else does, so a
/// scheduler with locks off installs nothing in its workers. They are sh scripts, as Git for Windows runs
/// hooks, with LF line ends. Each one runs the repository's own hook of the same name afterwards, if it has one
/// (the environment's <c>core.hooksPath</c> would otherwise hide it).
/// </summary>
public static class HookFiles
{
    /// <summary>The hook that appends <c>Card: &lt;card&gt;</c> (from <c>PTK_CARD</c>) to every commit.</summary>
    public const string CommitMsg = "commit-msg";

    /// <summary>The hook that runs <c>scheduler-next check-push</c> before a push.</summary>
    public const string PrePush = "pre-push";

    private static readonly UTF8Encoding Utf8 = new(false);

    // True (in sh) only in the project's own repository: PTK_REPO, the project's git folder, is this
    // repository's common git dir. Any other repository (a test's temp repo) is left alone (card 1039-B).
    private const string OwnRepo =
        "[ -n \"$PTK_REPO\" ] && [ \"$(git rev-parse --path-format=absolute --git-common-dir 2>/dev/null | tr 'A-Z\\' 'a-z/')\" = \"$(printf '%s' \"$PTK_REPO\" | tr 'A-Z\\' 'a-z/')\" ]";

    /// <summary>The <c>commit-msg</c> hook's text.</summary>
    public static string CommitMsgText() => Lf($$"""
        #!/bin/sh
        # Written by ProjectToolkit's scheduler at every start (dynamic locks, DL-18). Do not edit.
        # Appends the trailer "Card: <card>" from PTK_CARD, so every commit on main names its card.
        if [ -n "$PTK_CARD" ] && {{OwnRepo}}; then
          git interpret-trailers --in-place --if-exists addIfDifferent --trailer "Card: $PTK_CARD" "$1" || exit 1
        fi
        own="$(git rev-parse --git-common-dir 2>/dev/null)/hooks/commit-msg"
        if [ -f "$own" ] && [ -x "$own" ]; then
          exec "$own" "$@"
        fi
        exit 0
        """);

    /// <summary>
    /// The <c>pre-push</c> hook's text: git's ref lines go to <c>check-push</c>, run from
    /// <c>PTK_SCHEDULER_EXE</c> (else <paramref name="exe"/>), whose non-zero exit refuses the push.
    /// </summary>
    public static string PrePushText(string exe) => Lf($$"""
        #!/bin/sh
        # Written by ProjectToolkit's scheduler at every start (dynamic locks, DL-18). Do not edit.
        # Asks the scheduler whether this card may push what it pushes to main; a wip/ branch is never refused.
        refs="$(cat)"
        own="$(git rev-parse --git-common-dir 2>/dev/null)/hooks/pre-push"
        if [ -n "$PTK_CARD" ] && {{OwnRepo}}; then
          exe="${PTK_SCHEDULER_EXE:-{{Sh(exe)}}}"
          exe="$(printf '%s' "$exe" | tr '\\' '/')"
          if [ ! -f "$exe" ]; then
            echo "check-push: the scheduler's exe ($exe) is not there (being updated?); push again in a minute" >&2
            exit 1
          fi
          printf '%s\n' "$refs" | "$exe" check-push "$@" || exit 1
        fi
        if [ -f "$own" ] && [ -x "$own" ]; then
          printf '%s\n' "$refs" | "$own" "$@" || exit 1
        fi
        exit 0
        """);

    /// <summary>
    /// Writes both hooks into <paramref name="hooksDir"/>, replacing what an earlier start left. A hook whose
    /// text is already there is left alone (a worker's git may be running it: Windows refuses to replace a file
    /// a running <c>sh</c> has open, 1077-B). A changed one is written beside (<c>.tmp</c>) and renamed over,
    /// retried for about two seconds; if it is still in use the earlier file stays, which still enforces locks
    /// (same path, same checks), and the log says which hooks are current. Returns true only when both are current.
    /// </summary>
    public static bool Write(string hooksDir, string exe, ISchedulerLog? log) =>
        Write(hooksDir, exe, log, attempts: 10, delay: TimeSpan.FromMilliseconds(200));

    /// <summary>As <see cref="Write(string,string,ISchedulerLog)"/>, with the retry count and pause given.</summary>
    public static bool Write(string hooksDir, string exe, ISchedulerLog? log, int attempts, TimeSpan delay)
    {
        ArgumentException.ThrowIfNullOrEmpty(hooksDir);
        var states = new List<string>();
        string? error = null;
        try
        {
            Directory.CreateDirectory(hooksDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Write($"locks: git hooks could not be written to {hooksDir}: {e.Message}; no hooks folder");
            return false;
        }

        foreach (var (name, text) in new[] { (CommitMsg, CommitMsgText()), (PrePush, PrePushText(exe ?? "")) })
        {
            var path = Path.Combine(hooksDir, name);
            try
            {
                Put(path, text, attempts, delay);
                states.Add($"{name} current");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error ??= e.Message;
                states.Add($"{name} {(File.Exists(path) ? "older version kept, still enforcing locks" : "MISSING, locks not enforced")}");
            }
        }

        if (error is null)
        {
            log?.Write($"locks: git hooks written to {hooksDir} ({string.Join("; ", states)})");
            return true;
        }

        log?.Write($"locks: git hooks could not be written to {hooksDir}: {error} ({string.Join("; ", states)}; tried again at the next start)");
        return false;
    }

    private static void Put(string path, string text, int attempts, TimeSpan delay)
    {
        if (SameText(path, text)) return;
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, text, Utf8);
            for (var i = 1; ; i++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && i < attempts)
                {
                    Thread.Sleep(delay);
                }
            }
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static bool SameText(string path, string text)
    {
        try { return File.Exists(path) && File.ReadAllText(path, Utf8) == text; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // The exe path as a default inside "${...:-...}": forward slashes, and nothing sh would expand.
    private static string Sh(string exe) =>
        exe.Replace('\\', '/').Replace("\"", "", StringComparison.Ordinal).Replace("$", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal);

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
}
