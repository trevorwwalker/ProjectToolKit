using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

// Lists the processes that start while it runs (1041-B), for the starts the git shim cannot see (no elevation, so no
// ETW or Sysmon): it takes a snapshot of the process table every 10 ms (with each process's parent) and writes one
// line per new process: unix ms first seen, pid, parent pid, image name. The script keeps the processes below the
// one it measures. A process that starts and ends between two snapshots is missed, so counts are a lower bound;
// git starts by name are counted exactly by the shim.
//   ProcWatch <out-file> <stop-file>      runs until <stop-file> exists
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ProcWatch <out-file> <stop-file>");
    return 2;
}

using var outFile = new StreamWriter(new FileStream(args[0], FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
var previous = new HashSet<(uint, uint, string)>();
while (!File.Exists(args[1]))
{
    var current = Snapshot.Take();
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    foreach (var key in current)
    {
        if (!previous.Contains(key))
        {
            outFile.Write(string.Create(CultureInfo.InvariantCulture, $"{now}\t{key.Item1}\t{key.Item2}\t{key.Item3}\n"));
        }
    }

    previous = current;
    outFile.Flush();
    Thread.Sleep(10);
}

return 0;

internal static class Snapshot
{
    private const uint SnapProcess = 2;

    public static HashSet<(uint, uint, string)> Take()
    {
        var set = new HashSet<(uint, uint, string)>();
        var h = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (h == nint.Zero || h == -1)
        {
            return set;
        }

        try
        {
            var e = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var ok = Process32First(h, ref e); ok; ok = Process32Next(h, ref e))
            {
                set.Add((e.ProcessId, e.ParentProcessId, e.ExeFile));
            }
        }
        finally
        {
            CloseHandle(h);
        }

        return set;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
