// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Host.Program;

/// <summary>
/// <c>scheduler-next.exe --deliver-message &lt;format&gt;</c>, the agent hook that hands the
/// developer's message to a worker after each tool call (WRK-14, job card 570-B). With no message
/// waiting, no <see cref="MessageHook.FileVariable"/>, or an agent without hooks it prints nothing
/// and returns at once, so it is harmless in any other session.
/// </summary>
public static class MessageDelivery
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Moves the message file to its <c>.read.txt</c> name (as a worker reading it itself does),
    /// stamps that file with <paramref name="nowUtc"/> as the time it was read, and prints the
    /// agent's reply with the text. <paramref name="readInput"/> (the hook's JSON on stdin) is only
    /// read once there is a message. Always returns 0: a hook that fails must not fail the tool.
    /// </summary>
    public static int Run(string format, string? messageFile, Func<string> readInput, TextWriter output, DateTime nowUtc)
    {
        var agent = MessageHook.ParseFormat(format);
        if (agent == AgentFormat.Text)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(messageFile) || !File.Exists(messageFile))
        {
            return 0;
        }

        var readFile = MessageHook.ReadFileOf(messageFile);
        string message;
        try
        {
            // Moved first, then read: a message the page appends meanwhile starts a new file and
            // waits for the next tool call instead of being lost. A hook running beside this one
            // for another tool finds the file gone and prints nothing.
            File.Move(messageFile, readFile, overwrite: true);
            message = File.ReadAllText(readFile, Utf8);
            File.SetLastWriteTimeUtc(readFile, nowUtc);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        if (message.Trim().Length == 0)
        {
            return 0;
        }

        var reply = MessageHook.Reply(agent, readInput(), MessageHook.ContextText(message, readFile));
        if (reply is not null)
        {
            output.WriteLine(reply);
            output.Flush();
        }

        return 0;
    }

    /// <summary>
    /// Writes the Claude settings that install the hook (<see cref="WorkerFiles.HookSettings"/>)
    /// for <paramref name="exePath"/>; called when the scheduler starts, before any worker.
    /// </summary>
    public static void WriteClaudeSettings(string logDir, string exePath, ISchedulerLog log)
    {
        var path = WorkerFiles.HookSettings(logDir);
        try
        {
            Directory.CreateDirectory(logDir);
            File.WriteAllText(path, MessageHook.ClaudeSettings(exePath), Utf8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Write($"could not write the message hook settings {path} ({e.Message})");
        }
    }
}
