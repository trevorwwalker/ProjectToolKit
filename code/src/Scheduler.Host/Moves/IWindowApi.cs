namespace Scheduler.Host.Moves;

/// <summary>
/// The window calls bringing an opened file's window to the front needs (986-B, MOVE-9's
/// Raise-Editor, L1575): Windows' user32 in <see cref="Win32WindowApi"/>, a fake in the tests.
/// A window is a handle; 0 means none.
/// </summary>
public interface IWindowApi
{
    /// <summary>Open <paramref name="file"/> with its default program through the shell; the started process's id, or null when the shell handed the file to a running one. Throws when it cannot open it.</summary>
    int? Start(string file);

    /// <summary>Let process <paramref name="pid"/> take the foreground itself, where this process may grant it.</summary>
    void AllowForeground(int pid);

    /// <summary>The main window of process <paramref name="pid"/>; 0 when it has none (yet) or has ended.</summary>
    nint MainWindow(int pid);

    /// <summary>The topmost visible top-level window whose title contains <paramref name="text"/> (ignoring case); 0 when there is none.</summary>
    nint FindTitled(string text);

    /// <summary>Whether <paramref name="window"/> is minimized.</summary>
    bool IsMinimized(nint window);

    /// <summary>Restore <paramref name="window"/> from minimized.</summary>
    void Restore(nint window);

    /// <summary>Make <paramref name="window"/> the foreground window; false when Windows refuses.</summary>
    bool BringToFront(nint window);

    /// <summary>Flash <paramref name="window"/> in the taskbar until it is brought forward.</summary>
    void Flash(nint window);
}
