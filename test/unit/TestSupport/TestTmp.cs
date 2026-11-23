using System.Diagnostics;

namespace Scheduler.TestSupport;

/// <summary>
/// Scratch folders for tests, under <c>out\test-tmp\</c> in the repository (inside the Defender exclusion
/// for C:\Projects) instead of %TEMP%, where every object a test repository writes is scanned (card 1042-B).
/// Each test process owns one short run folder <c>r&lt;pid&gt;-&lt;start&gt;</c>; every call gets its own unique
/// child, so parallel tests never share a folder. The run folder is removed when the process exits, and
/// run folders left by a crashed process (owner no longer running) are removed at the start of the next run.
/// This file is linked into every test project by test/unit/Directory.Build.props.
/// </summary>
internal static class TestTmp
{
    private static readonly string Root = FindRoot();
    private static readonly string Run = StartRun();

    /// <summary>A new, empty folder with a unique short name; the caller deletes it (or leaves it to the exit sweep).</summary>
    internal static string Create(string prefix)
    {
        var path = NewPath(prefix);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A unique path that does not exist yet.</summary>
    internal static string NewPath(string prefix) =>
        Path.Combine(Run, prefix + "-" + Guid.NewGuid().ToString("N")[..8]);

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "code", "src", "SchedulerNext.slnx")))
            {
                return Path.Combine(d.FullName, "out", "test-tmp");
            }
        }

        throw new InvalidOperationException("Repository root (code/src/SchedulerNext.slnx) not found above " + AppContext.BaseDirectory);
    }

    private static string StartRun()
    {
        Directory.CreateDirectory(Root);
        SweepStale();
        var me = Process.GetCurrentProcess();
        var run = Path.Combine(Root, "r" + me.Id + "-" + me.StartTime.ToUniversalTime().Ticks.ToString("x"));
        Directory.CreateDirectory(run);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Remove(run);
        return run;
    }

    private static void SweepStale()
    {
        foreach (var dir in Directory.EnumerateDirectories(Root, "r*"))
        {
            if (!IsOwnerAlive(Path.GetFileName(dir)))
            {
                Remove(dir);
            }
        }
    }

    private static bool IsOwnerAlive(string name)
    {
        var parts = name[1..].Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var pid))
        {
            return false;
        }

        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime().Ticks.ToString("x") == parts[1];
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static void Remove(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
