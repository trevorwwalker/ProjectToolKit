// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Drawing;

namespace Scheduler.Tray;

/// <summary>Loads the tray icon (973-B): scheduler.ico from beside the exe, else today's stock icon with one log line.</summary>
public static class TrayIconLoader
{
    /// <summary>The icon file's name, copied beside the exe on build and publish.</summary>
    public const string FileName = "scheduler.ico";

    /// <summary>
    /// The icon file in <paramref name="folder"/> when present (an override), else the icon embedded in the
    /// assembly (993-B); <see cref="SystemIcons.Application"/> only if neither can be read.
    /// </summary>
    public static Icon Load(string folder, Action<string> log)
    {
        var path = Path.Combine(folder, FileName);
        try
        {
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log($"[tray] {FileName} could not be read ({ex.Message}); using the embedded icon");
        }

        try
        {
            using var stream = typeof(TrayIconLoader).Assembly.GetManifestResourceStream(FileName);
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            log($"[tray] the embedded {FileName} could not be read ({ex.Message}); using the default icon");
            return SystemIcons.Application;
        }

        log($"[tray] no {FileName} found; using the default icon");
        return SystemIcons.Application;
    }
}
