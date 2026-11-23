using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Scheduler.Host.Moves;

/// <summary>
/// <see cref="IWindowApi"/> on Windows' user32 through P/Invoke (986-B). The scheduler runs in the
/// background, so Windows' foreground lock usually refuses its <c>SetForegroundWindow</c>; a refused
/// call is tried once more with this thread's input attached to the foreground window's thread, which
/// Windows accepts as coming from the foreground.
/// </summary>
public sealed class Win32WindowApi : IWindowApi
{
    private const int SwRestore = 9;
    private const uint FlashwAll = 3;
    private const uint FlashwTimerNoFg = 12;

    private delegate bool EnumWindowsProc(nint window, nint param);

    /// <inheritdoc/>
    public int? Start(string file)
    {
        using var p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        if (p is null)
        {
            return null;
        }

        try
        {
            return p.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void AllowForeground(int pid) => AllowSetForegroundWindow((uint)pid);

    /// <inheritdoc/>
    public nint MainWindow(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited ? 0 : p.MainWindowHandle;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    public nint FindTitled(string text)
    {
        nint found = 0;
        var title = new StringBuilder(512);
        EnumWindowsProc each = (window, _) =>
        {
            if (!IsWindowVisible(window))
            {
                return true;
            }

            title.Clear();
            title.EnsureCapacity(512);
            if (GetWindowTextW(window, title, title.Capacity) > 0
                && title.ToString().Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                found = window;
                return false;
            }

            return true;
        };
        EnumWindows(each, 0);
        GC.KeepAlive(each);
        return found;
    }

    /// <inheritdoc/>
    public bool IsMinimized(nint window) => IsIconic(window);

    /// <inheritdoc/>
    public void Restore(nint window) => ShowWindow(window, SwRestore);

    /// <inheritdoc/>
    public bool BringToFront(nint window)
    {
        if (SetForegroundWindow(window) && GetForegroundWindow() == window)
        {
            return true;
        }

        var front = GetForegroundWindow();
        var theirs = front == 0 ? 0u : GetWindowThreadProcessId(front, out _);
        var mine = GetCurrentThreadId();
        var attached = theirs != 0 && theirs != mine && AttachThreadInput(mine, theirs, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(mine, theirs, false);
            }
        }

        return GetForegroundWindow() == window;
    }

    /// <inheritdoc/>
    public void Flash(nint window)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = window,
            Flags = FlashwAll | FlashwTimerNoFg,
            Count = uint.MaxValue,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint pid);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint pid);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint to, [MarshalAs(UnmanagedType.Bool)] bool on);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
