namespace Scheduler.Tray;

/// <summary>
/// The scheduler's version as a deploy reports it (967-B, VER rules): <c>Major.Minor.Rev</c>, the same
/// three numbers <c>CoreInfo.Version</c> shows on the page and in the scheduler's start line. An
/// informational version carries the source sha after a <c>+</c> and a file version a fourth number, and
/// neither belongs in the log window.
/// </summary>
public static class VersionText
{
    /// <summary>
    /// The version to show, from what the exe's file information says. The product version is the built
    /// one (VER-2), so it is used when it is there; the file version backs it up.
    /// </summary>
    public static string From(string? productVersion, string? fileVersion)
    {
        var text = Pick(productVersion) is { Length: > 0 } p ? p : Pick(fileVersion);
        return Trim(text);
    }

    /// <summary>Three numbers when the text has four, with nothing after a <c>+</c>.</summary>
    public static string Trim(string? version)
    {
        var text = (version ?? "").Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        var parts = text.Split('.');
        if (parts.Length > 3 && parts[3] is "0" or "00")
        {
            text = string.Join('.', parts[..3]);
        }

        return text.Trim();
    }

    private static string Pick(string? version)
    {
        var text = (version ?? "").Trim();
        return IsNumber(text) ? text : "";
    }

    private static bool IsNumber(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsDigit(c) && c != '.' && c != '+')
            {
                return false;
            }
        }

        return true;
    }
}
