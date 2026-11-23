using System.Diagnostics;
using System.Globalization;
using System.Text;

// A git.exe that counts (1041-B): put its folder first on PATH and every start of "git" by name is logged to the
// file named by PTK_GITLOG (one line: unix ms, pid, working folder, arguments), then the real git
// (PTK_REALGIT, default C:\Program Files\Git\cmd\git.exe) runs with the same arguments and standard handles.
// Starts by absolute path, and the processes git starts itself, are not seen here: ProcWatch sees those.
var log = Environment.GetEnvironmentVariable("PTK_GITLOG");
var real = Environment.GetEnvironmentVariable("PTK_REALGIT") ?? @"C:\Program Files\Git\cmd\git.exe";
if (!string.IsNullOrEmpty(log))
{
    var line = string.Create(CultureInfo.InvariantCulture,
        $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\t{Environment.ProcessId}\t{Environment.CurrentDirectory}\t{string.Join(' ', args).Replace('\t', ' ').Replace('\n', ' ')}\n");
    var bytes = Encoding.UTF8.GetBytes(line);
    for (var attempt = 0; attempt < 50; attempt++)
    {
        try
        {
            using var f = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            f.Write(bytes);
            break;
        }
        catch (IOException)
        {
            Thread.Sleep(5);
        }
    }
}

var psi = new ProcessStartInfo(real) { UseShellExecute = false };
foreach (var a in args)
{
    psi.ArgumentList.Add(a);
}

using var p = Process.Start(psi) ?? throw new InvalidOperationException("git did not start");
p.WaitForExit();
return p.ExitCode;
