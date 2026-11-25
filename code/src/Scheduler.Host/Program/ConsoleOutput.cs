// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;

namespace Scheduler.Host.Program;

/// <summary>
/// The console the scheduler writes its log to (670-B). The lines go to the tray's log window as
/// bytes, so the scheduler decides their encoding rather than leaving it to the console code page:
/// UTF-8 without a byte-order mark, which is what <c>Scheduler.Tray.SchedulerProcess</c> reads them back as.
/// </summary>
public static class ConsoleOutput
{
    /// <summary>The encoding the console lines are written in: UTF-8, no byte-order mark.</summary>
    public static Encoding Utf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Make stdout and stderr write <see cref="Utf8"/>. False when the console refused it, in which case
    /// the lines keep the code page the console had; nothing else about the run changes.
    /// </summary>
    public static bool TrySetUtf8()
    {
        try
        {
            Console.OutputEncoding = Utf8;
            return true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
