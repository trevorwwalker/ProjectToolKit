// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Slots;

/// <summary>A slot's stop, warn and message files in the log folder (WRK-6).</summary>
public sealed class SlotFiles
{
    private readonly string _logDir;

    /// <summary>Files live in <paramref name="logDir"/>.</summary>
    public SlotFiles(string logDir) => _logDir = logDir;

    /// <summary>The files a slot's worker is told by: stop, warn, message, and the read message.</summary>
    public IReadOnlyList<string> Paths(int slot) =>
    [
        Path.Combine(_logDir, $"stop-slot{slot}"),
        Path.Combine(_logDir, $"warn-slot{slot}"),
        Path.Combine(_logDir, $"msg-slot{slot}.txt"),
        Path.Combine(_logDir, $"msg-slot{slot}.read.txt"),
    ];

    /// <summary>Delete them (WRK-6).</summary>
    public void Clear(int slot)
    {
        foreach (var path in Paths(slot))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
