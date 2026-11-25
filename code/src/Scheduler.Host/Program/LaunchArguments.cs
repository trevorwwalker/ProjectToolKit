// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Program;

/// <summary>
/// MP-C9: <c>-Pool [&lt;file&gt;]</c>, taken out of the arguments before the rest go to the configuration's
/// command line (CFG-7). The name matches ignoring case, as PowerShell's do. (<c>-Observe</c> and
/// <c>-Compare</c> were retired by 1208-B, 2026-11-23.)
/// </summary>
public sealed record LaunchArguments
{
    /// <summary>MP-C9: <c>-Pool [&lt;file&gt;]</c> was given: run the pool, not one repository.</summary>
    public bool Pool { get; init; }

    /// <summary>The file after <c>-Pool</c>; "" = the default pool file (MP-C1).</summary>
    public string PoolFile { get; init; } = "";

    /// <summary>The other arguments, in order.</summary>
    public IReadOnlyList<string> Rest { get; init; } = [];

    /// <summary>Take <c>-Pool</c> out of <paramref name="args"/>.</summary>
    public static LaunchArguments Parse(IReadOnlyList<string> args)
    {
        var rest = new List<string>();
        var pool = false;
        var poolFile = "";
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "-Pool", StringComparison.OrdinalIgnoreCase))
            {
                // MP-C9: the file is optional; the next argument is it unless it is a switch.
                pool = true;
                if (i + 1 < args.Count && args[i + 1].Length > 0 && !args[i + 1].StartsWith('-'))
                {
                    poolFile = args[++i];
                }
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        return new LaunchArguments { Pool = pool, PoolFile = poolFile, Rest = rest };
    }

    /// <summary>1302-B: <c>--help</c>, <c>-h</c>, <c>-?</c> or <c>/?</c> (ignoring case) anywhere among the arguments.</summary>
    public static bool IsHelp(IReadOnlyList<string> args)
    {
        foreach (var a in args)
        {
            if (a.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || a.Equals("-h", StringComparison.OrdinalIgnoreCase)
                || a == "-?" || a == "/?")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>1302-B: the usage text <c>--help</c> prints: the name and version, each form and switch, the guide.</summary>
    public static string HelpText(string version) => string.Join("\n",
        $"scheduler-next {version}",
        "",
        "usage:",
        "usage:",
        "  scheduler-next <repository> [switches]    run the scheduler for one repository (or -Repo <repository>)",
        "  scheduler-next -Pool [<file>] [switches]  run every project of a pool file in one process",
        "  scheduler-next lock <path>... --why <text>   a worker asks for a file it does not hold",
        "  scheduler-next check-push                 the pre-push hook: does a push to main touch only held files",
        "  scheduler-next --deliver-message <file>   the agent hook that hands a worker the developer's message",
        "  scheduler-next --version                  print the version and exit",
        "  scheduler-next --help | -h | -? | /?      print this usage and exit",
        "",
        "switches:",
        "  -Repo <repository>   the repository (the same as giving it first)",
        "  -Config <file>       the scheduler's settings file",
        "  -Slots <n>           how many workers run at once",
        "  -Port <n>            the port of the status page",
        "  -Model <m>           the default model",
        "  -NoHealthCheck       do not check that main stays green",
        "  -Paused              start with no new cards taken",
        "  -DryRun              say what would be done; start no workers",
        "  -NoBrowser           do not open the status page in a browser",
        "  -PoolName:<name>     the name the pool's single-instance lock is made from",
        "  -ShutdownEvent:<name>  the event the tray owns, to ask the scheduler to stop",
        "  -Replaces:<version>  the version of the scheduler this one was started in place of",
        "",
        "More: docs/guide/scheduler-cli.md");
}
