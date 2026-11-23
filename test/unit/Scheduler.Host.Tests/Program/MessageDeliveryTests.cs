using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// <c>scheduler-next.exe --deliver-message</c>, the hook that hands a worker the developer's
/// message after each tool call (WRK-14, job card 570-B).
/// </summary>
public sealed class MessageDeliveryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 18, 23, 45, 12, DateTimeKind.Utc);

    private readonly string _dir = Scheduler.TestSupport.TestTmp.NewPath("msg");

    public MessageDeliveryTests() => Directory.CreateDirectory(_dir);

    private string MessageFile => WorkerFiles.MessageFile(_dir, 3);

    private string ReadFile => WorkerFiles.MessageReadFile(_dir, 3);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Run(string format, string? file, string input = "{}")
    {
        var output = new StringWriter();
        Assert.Equal(0, MessageDelivery.Run(format, file, () => input, output, Now));
        return output.ToString();
    }

    // WRK-14: a waiting message is printed as Claude's hook output and the file is moved to its
    // .read.txt name, stamped with the time it was read.
    [Fact]
    public void Deliver_Claude_PrintsAndMoves()
    {
        File.WriteAllText(MessageFile, "[23:44] use lane B only\n");

        var printed = Run("claude", MessageFile, """{"hook_event_name":"PostToolUseFailure"}""");

        var reply = JsonNode.Parse(printed)!["hookSpecificOutput"]!;
        Assert.Equal("PostToolUseFailure", (string?)reply["hookEventName"]);
        Assert.StartsWith("Developer: [23:44] use lane B only", (string?)reply["additionalContext"], StringComparison.Ordinal);
        Assert.False(File.Exists(MessageFile));
        Assert.Equal("[23:44] use lane B only\n", File.ReadAllText(ReadFile));
        Assert.Equal(Now, File.GetLastWriteTimeUtc(ReadFile));
    }

    // WRK-14: Copilot's output is a top-level additionalContext; an earlier read file is replaced.
    [Fact]
    public void Deliver_Copilot_PrintsAndMoves()
    {
        File.WriteAllText(ReadFile, "old");
        File.WriteAllText(MessageFile, "[23:44] hello");

        var printed = Run("copilot", MessageFile);

        Assert.StartsWith("Developer: [23:44] hello", (string?)JsonNode.Parse(printed)!["additionalContext"], StringComparison.Ordinal);
        Assert.Equal("[23:44] hello", File.ReadAllText(ReadFile));
    }

    // WRK-14: no message, no variable, or an agent without hooks: nothing printed, nothing moved,
    // and the hook's stdin is not even read.
    [Fact]
    public void Deliver_NothingToDo_PrintsNothing()
    {
        var output = new StringWriter();
        string Unread() => throw new InvalidOperationException("stdin read with nothing to deliver");

        Assert.Equal(0, MessageDelivery.Run("claude", MessageFile, Unread, output, Now));
        Assert.Equal(0, MessageDelivery.Run("claude", null, Unread, output, Now));
        Assert.Equal(0, MessageDelivery.Run("claude", "", Unread, output, Now));

        File.WriteAllText(MessageFile, "[23:44] hello");
        Assert.Equal(0, MessageDelivery.Run("text", MessageFile, Unread, output, Now));
        Assert.Equal(0, MessageDelivery.Run("", MessageFile, Unread, output, Now));

        Assert.Equal("", output.ToString());
        Assert.True(File.Exists(MessageFile));
    }

    // WRK-14: the scheduler writes the Claude settings its workers are given with --settings.
    [Fact]
    public void WriteClaudeSettings_WritesTheHook()
    {
        var log = new ListLog();

        MessageDelivery.WriteClaudeSettings(_dir, @"C:\Tools\scheduler-next.exe", log);

        var settings = JsonNode.Parse(File.ReadAllText(WorkerFiles.HookSettings(_dir)))!;
        Assert.Equal(
            "\"C:/Tools/scheduler-next.exe\" --deliver-message claude",
            (string?)settings["hooks"]!["PostToolUse"]![0]!["hooks"]![0]!["command"]);
        Assert.Empty(log.Lines);
    }

    private sealed class ListLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }
}
