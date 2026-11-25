// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Program;

/// <summary>
/// <c>-ShutdownEvent:&lt;name&gt;</c> (670-B): the name of a manual-reset event the tray owns, which is how
/// the tray asks this scheduler to end by itself - the "scheduler's own clean shutdown path" - instead of
/// a console Ctrl+C, which would reach every process sharing that console, the workers among them. The
/// scheduler's command line (CFG-7) leaves a switch it does not bind alone, so this one is read at the
/// entry point. Names match ignoring case, as PowerShell's do.
/// </summary>
public static class ShutdownSwitch
{
    /// <summary>The switch as it is typed.</summary>
    public const string Name = "-ShutdownEvent";

    /// <summary>The event's name, or "" when the switch was not given.</summary>
    public static string EventName(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            var name = arg;
            string? value = null;
            var split = arg.IndexOfAny([':', '=']);
            if (split > 1)
            {
                name = arg[..split];
                value = arg[(split + 1)..];
            }

            if (Text.Same(name, Name) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
