namespace Scheduler.Tray;

/// <summary>
/// The folders a deploy works in (967-B, BUILD-2). The scheduler runs from <c>out\windows\scheduler</c>;
/// a new build is published beside it into <c>out\windows\scheduler.next</c> while the old one keeps
/// running, and the build the swap replaces is kept in <c>out\windows\scheduler.prev</c> in case the new
/// one will not start. Nothing moves the running path, so the page's inbound firewall rule (BUILD-3),
/// which is bound to <c>out\windows\scheduler\scheduler-next.exe</c>, still matches after a deploy.
/// </summary>
public sealed class DeployLayout(string outWindowsDir)
{
    /// <summary>The folder the scheduler runs from.</summary>
    public const string SchedulerFolderName = "scheduler";

    /// <summary>The folder a new build is published into beside the running one.</summary>
    public const string NextFolderName = "scheduler.next";

    /// <summary>The folder the build the swap replaced is kept in.</summary>
    public const string PrevFolderName = "scheduler.prev";

    /// <summary>The scheduler's own exe - the path the firewall rule is bound to (BUILD-3).</summary>
    public const string SchedulerExeName = "scheduler-next.exe";

    /// <summary><c>...\out\windows</c>, the folder holding the three.</summary>
    public string OutWindowsDir { get; } = Path.GetFullPath(outWindowsDir).TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);

    /// <summary>The folder the scheduler runs from.</summary>
    public string Current => Path.Combine(OutWindowsDir, SchedulerFolderName);

    /// <summary>The folder the new build is published into.</summary>
    public string Next => Path.Combine(OutWindowsDir, NextFolderName);

    /// <summary>The folder the replaced build is kept in.</summary>
    public string Prev => Path.Combine(OutWindowsDir, PrevFolderName);

    /// <summary>The exe the page's firewall rule names; it never changes (BUILD-3).</summary>
    public string SchedulerExe => Path.Combine(Current, SchedulerExeName);

    /// <summary>The new build's scheduler exe, before the swap.</summary>
    public string NextSchedulerExe => Path.Combine(Next, SchedulerExeName);

    /// <summary>The kit's root: the folder holding <c>out\</c> and <c>code\</c>.</summary>
    public string KitRoot => Path.GetFullPath(Path.Combine(OutWindowsDir, "..", ".."));

    /// <summary>The project <c>dotnet publish</c> is given to make the runnable folder (BUILD-2).</summary>
    public string HostProject => Path.Combine(KitRoot, "code", "src", "Scheduler.Host");

    /// <summary>Whether the layout points at a real scheduler folder with something to deploy.</summary>
    public bool IsDeployable(ISwapFileSystem fs) =>
        fs.DirectoryExists(Current) && fs.DirectoryExists(Next) && fs.FileExists(NextSchedulerExe);

    /// <summary>The layout a published scheduler's exe path belongs to.</summary>
    public static DeployLayout ForSchedulerExe(string exePath) => ForSchedulerDir(ParentOf(exePath));

    /// <summary>The layout a running scheduler's folder belongs to.</summary>
    public static DeployLayout ForSchedulerDir(string schedulerDir) => new(ParentOf(schedulerDir));

    /// <summary>The layout of a kit's <c>out\windows</c>.</summary>
    public static DeployLayout ForKit(string kitRoot) => new(Path.Combine(kitRoot, "out", "windows"));

    private static string ParentOf(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return dir is { Length: > 0 } ? dir : full;
    }
}
