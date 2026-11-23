using Scheduler.Core.Contracts;

namespace Scheduler.Host.Program;

/// <summary>
/// <c>-NoBrowser</c> (670-B): the page is not opened at start, so a scheduler the tray started opens
/// nothing of itself and the tray's double-click is the way in (CFG-15). The scheduler's own command
/// line (CFG-7) leaves a switch it does not bind alone, so this one is read here, at the entry point
/// that decides to open the page. Names match ignoring case, as PowerShell's do, and a switch that
/// carries its value (<c>-NoBrowser:$false</c>) leaves the page alone only when that value is false.
/// </summary>
public static class NoBrowserSwitch
{
    /// <summary>The switch as it is typed.</summary>
    public const string Name = "-NoBrowser";

    /// <summary>Whether the arguments ask for the page not to be opened.</summary>
    public static bool IsSet(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (Is(arg))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Is(string arg)
    {
        var name = arg;
        string? value = null;
        var split = arg.IndexOfAny([':', '=']);
        if (split > 1)
        {
            name = arg[..split];
            value = arg[(split + 1)..];
        }

        if (!Text.Same(name, Name))
        {
            return false;
        }

        return value is null
            || !(Text.Same(value, "false") || Text.Same(value, "$false") || value == "0");
    }
}
