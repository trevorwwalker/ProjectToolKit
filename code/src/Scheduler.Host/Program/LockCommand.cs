// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Launch;
using Scheduler.Core.Locks;
using Scheduler.Core.Moves;

namespace Scheduler.Host.Program;

/// <summary>
/// Dynamic locks, the worker's side (design <c>dynamic-locks.md</c>, section 3; card 1020-B):
/// <c>scheduler-next lock &lt;path&gt; [&lt;path&gt; ...] --why "&lt;one line&gt;"</c>. It writes
/// <c>&lt;log&gt;\moves\&lt;time&gt;-slot&lt;N&gt;.lock</c> (tmp then rename) and waits for
/// <c>moves\replies\&lt;same name&gt;.reply</c>, printing a line every 30 seconds; its last line and exit code
/// are the answer: 0 granted, 2 waiting (or no answer yet), 3 yield, 4 refused. Run again while the card
/// has an open request (or its earlier request is still unanswered), it waits on that one and writes no new
/// request. It reads and writes files only and never starts a scheduler.
/// </summary>
public static class LockCommand
{
    /// <summary>The subcommand's name.</summary>
    public const string Switch = "lock";

    /// <summary>The log folder, set by the scheduler in the worker's environment.</summary>
    public const string LogDirVariable = "PTK_LOG_DIR";

    /// <summary>The worker's slot, set by the scheduler in the worker's environment.</summary>
    public const string SlotVariable = "PTK_SLOT";

    /// <summary>How long one call waits for its answer (under the prompt's two-minute rule).</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(110);

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// Runs the command. <paramref name="args"/> are those after <c>lock</c>; <paramref name="env"/> reads
    /// the environment; <paramref name="now"/> and <paramref name="sleep"/> are the clock (tests pass their own).
    /// </summary>
    public static int Run(
        IReadOnlyList<string> args,
        Func<string, string?> env,
        TextWriter output,
        Func<DateTimeOffset>? now = null,
        Action<TimeSpan>? sleep = null,
        TimeSpan? wait = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(output);
        now ??= () => DateTimeOffset.Now;
        sleep ??= Thread.Sleep;
        int Refuse(string why)
        {
            output.WriteLine($"refused: {why}");
            return LockReply.ExitCode(LockAnswer.Refused);
        }

        var (paths, why) = Parse(args);
        if (paths.Count == 0)
        {
            return Refuse("no paths: lock <path> [<path> ...] --why \"<reason>\"");
        }

        var card = env(WorkerLocks.CardVariable) ?? "";
        if (card.Length == 0)
        {
            return Refuse($"unknown card: {WorkerLocks.CardVariable} is not set (run it from a worker the scheduler started)");
        }

        var logDir = LogDir(env);
        if (logDir.Length == 0 || !Directory.Exists(logDir))
        {
            return Refuse($"no scheduler log folder: {LogDirVariable} is not set");
        }

        var row = Row(logDir, card);
        int? slot = int.TryParse(env(SlotVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : row?.Slot;
        var moves = CardRequests.Folder(logDir);
        var name = row?.Open?.FileName ?? Unanswered(moves, card) ?? Write(moves, card, slot ?? 0, paths, why, now());

        // Wait for the reply: a waiting one may still turn into granted or yield within this call.
        var reply = Path.Combine(moves, LockRequests.RepliesFolder, LockRequests.ReplyName(name));
        var start = now();
        var until = start + (wait ?? Wait);
        var nextLine = start + TimeSpan.FromSeconds(30);
        var last = "";
        while (true)
        {
            var text = ReadReply(reply);
            var answer = LockReply.Parse(text);
            if (answer is { } a && a != LockAnswer.Waiting)
            {
                output.WriteLine(text);
                return LockReply.ExitCode(a);
            }

            if (answer == LockAnswer.Waiting)
            {
                last = text;
            }

            var t = now();
            if (t >= until)
            {
                output.WriteLine(last.Length > 0 ? last : "waiting: no answer yet; run the same command again");
                return LockReply.ExitCode(LockAnswer.Waiting);
            }

            if (t >= nextLine)
            {
                output.WriteLine(last.Length > 0 ? $"still {last}" : "asked; no answer yet");
                nextLine = t + TimeSpan.FromSeconds(30);
            }

            sleep(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>The paths and the reason: every argument before <c>--why</c> is a path, the rest the reason.</summary>
    internal static (IReadOnlyList<string> Paths, string Why) Parse(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        var why = new List<string>();
        var inWhy = false;
        foreach (var a in args)
        {
            if (!inWhy && (a == "--why" || a == "-why" || a == "-Why"))
            {
                inWhy = true;
            }
            else if (inWhy)
            {
                why.Add(a);
            }
            else if (a.Trim().Length > 0)
            {
                paths.Add(a.Trim().Replace('\\', '/'));
            }
        }

        return (paths, string.Join(' ', why).Replace('\n', ' ').Replace('\r', ' ').Trim());
    }

    /// <summary>
    /// The log folder: <c>PTK_LOG_DIR</c>, else the parent of the hooks folder git is pointed at
    /// (<c>core.hooksPath</c> = <c>&lt;log&gt;\hooks</c>, DL-18).
    /// </summary>
    internal static string LogDir(Func<string, string?> env)
    {
        if (env(LogDirVariable) is { Length: > 0 } dir)
        {
            return dir;
        }

        var count = int.TryParse(env("GIT_CONFIG_COUNT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(env($"GIT_CONFIG_KEY_{i}"), "core.hooksPath", StringComparison.OrdinalIgnoreCase)
                && env($"GIT_CONFIG_VALUE_{i}") is { Length: > 0 } hooks)
            {
                return Path.GetDirectoryName(hooks.TrimEnd('\\', '/')) ?? "";
            }
        }

        return "";
    }

    /// <summary>The card's row in any lock table under the log folder, or null.</summary>
    internal static CardLocks? Row(string logDir, string card)
    {
        var dir = Path.Combine(logDir, LockTableText.FolderName);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var (table, _) = LockTableText.Read(File.ReadAllText(file, Utf8));
                if (table.Find(card) is { } row)
                {
                    return row;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // being replaced: the request is answered all the same
            }
        }

        return null;
    }

    /// <summary>The card's own request still waiting in the moves folder (not answered yet), or null.</summary>
    private static string? Unanswered(string moves, string card)
    {
        if (!Directory.Exists(moves))
        {
            return null;
        }

        foreach (var name in LockRequests.Order(Directory.EnumerateFiles(moves, "*" + LockRequests.Extension).Select(f => Path.GetFileName(f)!)))
        {
            try
            {
                if (Scheduler.Core.Contracts.Text.Same(LockRequests.Parse(name, File.ReadAllText(Path.Combine(moves, name), Utf8)).Card, card))
                {
                    return name;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // answered and filed away while being read
            }
        }

        return null;
    }

    private static string Write(string moves, string card, int slot, IReadOnlyList<string> paths, string why, DateTimeOffset at)
    {
        Directory.CreateDirectory(moves);
        var name = LockRequests.FileName(at, slot);
        var path = Path.Combine(moves, name);
        File.WriteAllText(path + ".tmp", LockRequests.Text(card, slot, paths, why.Length > 0 ? why : "(no reason given)"), Utf8);
        File.Move(path + ".tmp", path, overwrite: true);
        return name;
    }

    private static string ReadReply(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8).Trim().TrimStart('﻿') : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
