using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// The files a worker is started with (WRK-9, <c>Start-Worker</c> L1814-1822) and the three flag
/// files it watches (<c>Stop-Flag</c>/<c>Warn-Flag</c>/<c>Msg-File</c> L685-689), all in the log folder.
/// </summary>
public static class WorkerFiles
{
    /// <summary>
    /// The log, view and prompt file names for a card started at <paramref name="start"/>, and the
    /// pointer sentence the command line passes in place of the brief (cmd takes 8191 characters).
    /// </summary>
    public static WorkerLogFiles Names(SchedulerConfig config, Card card, DateTimeOffset start, AgentFormat format)
    {
        var json = format != AgentFormat.Text;
        var raw = Path.Combine(
            config.LogDir,
            $"{card.Id}-{start.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{(json ? "jsonl" : "log")}");
        var view = json ? Path.ChangeExtension(raw, ".log") : raw;
        var promptFile = Path.ChangeExtension(raw, ".prompt.txt");
        var pointer = $"You are a worker for {config.Project}. Your whole brief is in the file {promptFile} : "
            + "read all of it first (Get-Content -Raw -Encoding UTF8), then follow it exactly. "
            + "Do not edit, move or commit that file.";

        return new WorkerLogFiles { RawLog = raw, View = view, PromptFile = promptFile, Pointer = pointer };
    }

    /// <summary>The file that tells a worker to stop cleanly (WRK-6, TIME-9).</summary>
    public static string StopFlag(string logDir, int slot) => Path.Combine(logDir, $"stop-slot{slot}");

    /// <summary>The file that tells a worker its time limit is near (TIME-4).</summary>
    public static string WarnFlag(string logDir, int slot) => Path.Combine(logDir, $"warn-slot{slot}");

    /// <summary>The file the page appends the developer's messages to (WRK-14).</summary>
    public static string MessageFile(string logDir, int slot) => Path.Combine(logDir, $"msg-slot{slot}.txt");

    /// <summary>Where a worker moves the message file once it has read it.</summary>
    public static string MessageReadFile(string logDir, int slot) => Path.Combine(logDir, $"msg-slot{slot}.read.txt");

    /// <summary>
    /// The Claude settings file that installs the message hook (WRK-14, job card 570-B): written by
    /// the scheduler when it starts, passed to every Claude worker with <c>--settings</c>.
    /// </summary>
    public static string HookSettings(string logDir) => Path.Combine(logDir, "worker-hooks.claude.json");

    /// <summary>
    /// Dynamic locks (DL-18): the folder of the scheduler's own git hooks (<c>commit-msg</c>, <c>pre-push</c>),
    /// which a worker's git uses through <c>core.hooksPath</c> in its environment.
    /// </summary>
    public static string HooksDir(string logDir) => Path.Combine(logDir, "hooks");
}
