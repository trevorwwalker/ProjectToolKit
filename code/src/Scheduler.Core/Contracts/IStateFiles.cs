namespace Scheduler.Core.Contracts;

/// <summary>
/// The log folder's state files as text; Core builds and parses the text. Used by retries
/// (TIME-22), accepted categories (LANE-2, LANE-3), enabled slots (WRK-3), running.json and
/// adoption (ADOPT-1, ADOPT-2) and status.txt (ADOPT-3).
/// </summary>
public interface IStateFiles
{
    /// <summary>The file's text, or null when it does not exist or cannot be read.</summary>
    string? Read(StateFile file);

    /// <summary>Replace the file's text.</summary>
    void Write(StateFile file, string text);
}
