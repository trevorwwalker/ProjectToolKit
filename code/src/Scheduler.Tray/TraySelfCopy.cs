namespace Scheduler.Tray;

/// <summary>
/// Where the tray runs from (967-B): a copy of itself in <c>%LOCALAPPDATA%\ProjectToolkit\tray\</c>. The
/// published folder <c>out\windows\scheduler</c> then holds no running image of the tray, so "Update and
/// restart" can move every file in it while the tray that is asking sits in its own folder. Started from
/// the published folder, the tray copies itself there and runs the copy with the same arguments plus
/// <c>-SchedulerDir:</c> pointing back at where the scheduler is.
/// </summary>
public static class TraySelfCopy
{
    /// <summary>The file name the tray's copy is started from.</summary>
    public const string TrayExeName = "scheduler-tray.exe";

    /// <summary>The environment variable that says "this tray is already the copy", which ends any relaunch loop.</summary>
    public const string FromVariable = "PTK_TRAY_FROM";

    /// <summary>Whether a file name belongs to the tray rather than to the scheduler.</summary>
    public static bool IsTrayFile(string name) =>
        name.StartsWith("scheduler-tray", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Scheduler.Tray.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a file goes into the tray's copy (993-B): the tray's files and any icon.</summary>
    public static bool IsCopyFile(string name) =>
        IsTrayFile(name) || name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>%LOCALAPPDATA%\ProjectToolkit\tray</c>, the folder the tray runs from.</summary>
    public static string Folder(string? localAppData = null)
    {
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        return Path.Combine(root, "ProjectToolkit", "tray");
    }

    /// <summary>The tray exe inside the copy's folder.</summary>
    public static string Exe(string folder) => Path.Combine(folder, TrayExeName);

    /// <summary>
    /// Whether this tray is running somewhere other than the copy's folder and has not been relaunched
    /// already, so it should copy itself and run the copy.
    /// </summary>
    public static bool NeedsCopy(string baseDir, string? target = null, string? fromVariable = null)
    {
        var already = fromVariable ?? Environment.GetEnvironmentVariable(FromVariable);
        if (!string.IsNullOrEmpty(already))
        {
            return false;
        }

        return !SameDir(baseDir, target ?? Folder());
    }

    /// <summary>
    /// Copy the tray's own files into the copy's folder, overwriting the last version of them. True when
    /// the copy's exe is there afterwards.
    /// </summary>
    public static bool Copy(ISwapFileSystem fs, string baseDir, string target)
    {
        if (!fs.DirectoryExists(baseDir))
        {
            return false;
        }

        foreach (var entry in fs.Entries(baseDir))
        {
            var name = Path.GetFileName(entry);
            if (IsCopyFile(name) && !fs.IsDirectory(entry))
            {
                fs.Copy(entry, Path.Combine(target, name));
            }
        }

        return fs.FileExists(Exe(target));
    }

    /// <summary>
    /// The tray's own files that are in a folder: the names a swap must leave where they are, because this
    /// running tray is the image Windows holds onto (967-B).
    /// </summary>
    public static IReadOnlyList<string> Names(ISwapFileSystem fs, string dir)
    {
        var names = new List<string>();
        foreach (var entry in fs.Entries(dir))
        {
            var name = Path.GetFileName(entry);
            if (IsTrayFile(name) && !fs.IsDirectory(entry))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Whether two paths name the same folder, however they are spelled.</summary>
    public static bool SameDir(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
