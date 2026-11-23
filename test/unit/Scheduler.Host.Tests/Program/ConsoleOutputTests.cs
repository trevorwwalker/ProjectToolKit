using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// The scheduler writes its console in UTF-8 so the tray's log window shows what a UTF-8 console shows
/// (670-B). No byte-order mark: the tray reads the lines as they come, and a mark would sit in front of
/// the first log line.
/// </summary>
public class ConsoleOutputTests
{
    [Fact]
    public void The_console_encoding_is_UTF8_without_a_byte_order_mark()
    {
        Assert.Equal("utf-8", ConsoleOutput.Utf8.WebName);
        Assert.Empty(ConsoleOutput.Utf8.GetPreamble());
    }

    [Fact]
    public void The_console_takes_UTF8()
    {
        Assert.True(ConsoleOutput.TrySetUtf8());
        Assert.Equal("utf-8", Console.OutputEncoding.WebName);
        Assert.Empty(Console.OutputEncoding.GetPreamble());
    }
}
