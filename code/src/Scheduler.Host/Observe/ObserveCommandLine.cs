namespace Scheduler.Host.Observe;

/// <summary>
/// <c>-Observe</c> and <c>-Compare &lt;old log&gt; &lt;new log&gt;</c> (roadmap item 1), taken out of the
/// arguments before the rest go to the configuration's command line (CFG-7). Names match ignoring
/// case, as PowerShell's do.
/// </summary>
public sealed record ObserveCommandLine
{
    /// <summary><c>-Observe</c> was given.</summary>
    public bool Observe { get; init; }

    /// <summary><c>-Compare</c>'s first log, the old scheduler's; "" when not given.</summary>
    public string CompareOld { get; init; } = "";

    /// <summary><c>-Compare</c>'s second log, the observe run's.</summary>
    public string CompareNew { get; init; } = "";

    /// <summary><c>-Compare</c> was given.</summary>
    public bool Compare => CompareOld.Length > 0;

    /// <summary>MP-C9: <c>-Pool [&lt;file&gt;]</c> was given: run the pool, not one repository.</summary>
    public bool Pool { get; init; }

    /// <summary>The file after <c>-Pool</c>; "" = the default pool file (MP-C1).</summary>
    public string PoolFile { get; init; } = "";

    /// <summary>The other arguments, in order.</summary>
    public IReadOnlyList<string> Rest { get; init; } = [];

    /// <summary>What is wrong with the arguments; "" when nothing is.</summary>
    public string Error { get; init; } = "";

    /// <summary>Take <c>-Observe</c> and <c>-Compare</c> out of <paramref name="args"/>.</summary>
    public static ObserveCommandLine Parse(IReadOnlyList<string> args)
    {
        var rest = new List<string>();
        var observe = false;
        var pool = false;
        var poolFile = "";
        string old = "", @new = "";
        for (var i = 0; i < args.Count; i++)
        {
            if (Is(args[i], "-Observe"))
            {
                observe = true;
            }
            else if (Is(args[i], "-Pool"))
            {
                // MP-C9: the file is optional; the next argument is it unless it is a switch.
                pool = true;
                if (i + 1 < args.Count && args[i + 1].Length > 0 && !args[i + 1].StartsWith('-'))
                {
                    poolFile = args[++i];
                }
            }
            else if (Is(args[i], "-Compare"))
            {
                if (i + 2 >= args.Count || args[i + 1].Length == 0 || args[i + 2].Length == 0)
                {
                    return new ObserveCommandLine { Error = "-Compare needs two logs: the old scheduler's and the observe run's" };
                }

                old = args[i + 1];
                @new = args[i + 2];
                i += 2;
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        if (observe && old.Length > 0)
        {
            return new ObserveCommandLine { Error = "-Observe and -Compare cannot be used together" };
        }

        if (pool && (observe || old.Length > 0))
        {
            return new ObserveCommandLine { Error = "-Pool cannot be used with -Observe or -Compare" };
        }

        return new ObserveCommandLine { Observe = observe, CompareOld = old, CompareNew = @new, Pool = pool, PoolFile = poolFile, Rest = rest };
    }

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
}
