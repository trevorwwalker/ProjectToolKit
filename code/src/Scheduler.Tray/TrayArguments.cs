namespace Scheduler.Tray;

/// <summary>
/// The tray's command line: <c>scheduler-tray &lt;repository&gt; [the scheduler's own arguments]</c>.
/// Every argument goes to <c>scheduler-next.exe</c> unchanged; the tray only needs the repository to be there.
/// </summary>
public sealed record TrayArguments(string Repository, IReadOnlyList<string> SchedulerArgs, string Error, string SchedulerDir = "", bool Pool = false, string PoolFile = "", bool Quiet = false)
{
    /// <summary>The usage line shown when the arguments are wrong.</summary>
    public const string Usage = "usage: scheduler-tray <repository> [the scheduler's own arguments, as scheduler-next takes them]\nor:    scheduler-tray -Pool [<pool file>] [the scheduler's own arguments]";

    /// <summary>Whether the arguments can start the scheduler.</summary>
    public bool Ok => Error.Length == 0;

    /// <summary>Reads the command line.</summary>
    public static TrayArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return new TrayArguments("", [], "no repository given");
        }

        if (IsSwitch(args[0], PoolSwitch))
        {
            return ParsePool(args);
        }

        if (args[0].StartsWith('-'))
        {
            return new TrayArguments("", [], $"the first argument must be the repository, not {args[0]}");
        }

        // The tray's own switches are taken out before the rest is handed to the scheduler (967-B).
        var given = new List<string>();
        foreach (var arg in args)
        {
            if (!IsSwitch(arg, SchedulerDirSwitch) && !IsSwitch(arg, QuietSwitch))
            {
                given.Add(arg);
            }
        }

        return new TrayArguments(args[0], SchedulerArgsFor(given), "", ValueOf(args, SchedulerDirSwitch), Quiet: HasQuiet(args));
    }

    /// <summary>
    /// The switch that tells the tray where <c>scheduler-next.exe</c> is (967-B). The tray set it on its
    /// own copy when it relaunched itself from <c>%LOCALAPPDATA%\ProjectToolkit\tray\</c>; without it the
    /// scheduler is the exe beside the tray (TRAY-1).
    /// </summary>
    public const string SchedulerDirSwitch = "-SchedulerDir";

    /// <summary>The value of a switch that may carry it with a colon or an equals sign, or "" when it is not there.</summary>
    public static string ValueOf(IReadOnlyList<string> args, string name)
    {
        foreach (var arg in args)
        {
            if (!IsSwitch(arg, name))
            {
                continue;
            }

            var split = arg.IndexOfAny([':', '=']);
            return split > 1 ? arg[(split + 1)..].Trim() : "";
        }

        return "";
    }

    /// <summary>MP-H9: <c>-Pool [&lt;file&gt;]</c> starts the pool of the pool file instead of a repository.</summary>
    public const string PoolSwitch = "-Pool";

    // MP-H9: the pool file is the argument after -Pool unless that is a switch; the scheduler gets every argument as given.
    private static TrayArguments ParsePool(IReadOnlyList<string> args)
    {
        var file = args.Count > 1 && args[1].Length > 0 && !args[1].StartsWith('-') ? args[1] : "";
        var given = args.Where(a => !IsSwitch(a, SchedulerDirSwitch) && !IsSwitch(a, QuietSwitch)).ToList();
        return new TrayArguments("", SchedulerArgsFor(given), "", ValueOf(args, SchedulerDirSwitch), Pool: true, PoolFile: file, Quiet: HasQuiet(args));
    }

    /// <summary>
    /// 1014-B: the switch the tray's own automatic starts pass (the login entry, a deploy bringing the tray back):
    /// without it the tray opens the page once, as a start by hand should. It is the tray's alone, not the scheduler's.
    /// </summary>
    public const string QuietSwitch = "-Quiet";

    private static bool HasQuiet(IReadOnlyList<string> args) => args.Any(a => IsSwitch(a, QuietSwitch));

    /// <summary>The switch that keeps the scheduler's page in the browser shut (670-B).</summary>
    public const string NoBrowserSwitch = "-NoBrowser";

    // 670-B: the tray's own double-click is how the page is opened, so the scheduler is told not to open
    // it at start - unless the person starting the tray already said so, or asked for the opposite.
    private static IReadOnlyList<string> SchedulerArgsFor(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (IsSwitch(arg, NoBrowserSwitch))
            {
                return [.. args];
            }
        }

        return [.. args, NoBrowserSwitch];
    }

    private static bool IsSwitch(string arg, string name)
    {
        var split = arg.IndexOfAny([':', '=']);
        var given = split > 1 ? arg[..split] : arg;
        return string.Equals(given, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The project's name for the tooltip before the scheduler has said it: the repository folder's name.</summary>
    public string ProjectGuess()
    {
        if (Pool)
        {
            return "pool";
        }

        var trimmed = Repository.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }
}
