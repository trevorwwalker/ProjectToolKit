using System.Diagnostics;

namespace Scheduler.Tray;

/// <summary>
/// <see cref="IVersionReader"/> that asks Windows what the exe says it is (967-B). The number is the one
/// <c>&lt;Version&gt;</c> in code/src/Directory.Build.props built in (VER-2), so the log window can name
/// the two builds apart without either of them being run.
/// </summary>
public sealed class ExecutableVersionReader : IVersionReader
{
    /// <inheritdoc/>
    public string Read(string exePath)
    {
        if (!File.Exists(exePath))
        {
            return "";
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return VersionText.From(info.ProductVersion, info.FileVersion);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }
}
