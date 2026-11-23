using System.Runtime.InteropServices;

namespace Scheduler.Tray;

/// <summary>
/// The Ctrl+C the developer presses when they stop a scheduler by hand (670-B): the console event that
/// ends the scheduler on its own, so it closes its page and writes its status file, and that leaves the
/// workers alone - each is started in a console of its own, so the event does not reach it - to be
/// adopted by the next scheduler. The tray has no console of its own, so it borrows the scheduler's for
/// the moment it takes to send the event, and asks Windows not to let the event end the tray as well.
/// </summary>
public static class ConsoleSignal
{
    private const uint CtrlCEvent = 0;

    // The event runs the console's handler list on a thread of its own in every attached process, this
    // one included, and the default handler ends a process that does not claim the event. A managed
    // handler that says "handled" keeps the tray alive through the moment it shares the console - which is
    // why Restart used to take the tray down with the scheduler and start nothing. Held in a field so the
    // delegate is not collected while the native side can still call it.
    private static readonly ConsoleCtrlHandler Ignore = _ => true;

    private delegate bool ConsoleCtrlHandler(uint ctrlType);

    /// <summary>
    /// Send a Ctrl+C to the console of the process <paramref name="pid"/>. False when it could not be
    /// sent - the process has no console, this process already has one of its own, or it has already
    /// gone - which is the caller's cue to end it another way.
    /// </summary>
    public static bool TrySendCtrlC(int pid)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0)
        {
            return false;
        }

        try
        {
            // Borrowing a console means giving up the one we have, which a program that runs from a
            // terminal would not thank us for; the tray has none to lose.
            if (HasConsole())
            {
                return false;
            }

            if (!AttachConsole((uint)pid))
            {
                return false;
            }

            try
            {
                // The tray shares the console for this moment: claim the event so the tray is not ended with it.
                SetConsoleCtrlHandler(Ignore, true);
                var sent = GenerateConsoleCtrlEvent(CtrlCEvent, 0);

                // The event is still being handed to the other processes when this returns; detaching at
                // once has been seen to leave this process holding the console's default handler.
                Thread.Sleep(200);
                return sent;
            }
            finally
            {
                SetConsoleCtrlHandler(null, false);
                FreeConsole();
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or MarshalDirectiveException)
        {
            return false;
        }
    }

    /// <summary>Whether this process is attached to a console of its own.</summary>
    private static bool HasConsole()
    {
        var pids = new uint[4];
        return GetConsoleProcessList(pids, (uint)pids.Length) > 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processListSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);
}
