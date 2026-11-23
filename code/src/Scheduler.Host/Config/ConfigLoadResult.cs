using Scheduler.Core.Contracts;

namespace Scheduler.Host.Config;

/// <summary>
/// What finding the repository and the config file came up with (CFG-1 to CFG-4): the resolved
/// paths, the config itself once everything worked, why start-up failed when it did not, and the
/// lines the loader logged on the way. A fatal failure has <see cref="Ok"/> false, no
/// <see cref="Config"/> and an <see cref="Error"/> for the console; nothing is created for it.
/// </summary>
public sealed record ConfigLoadResult
{
    /// <summary>True when the repository and a usable config file were found.</summary>
    public bool Ok { get; init; }

    /// <summary>Why start-up must stop; "" when it need not.</summary>
    public string Error { get; init; } = "";

    /// <summary>The repository's top level as far as it was resolved (CFG-1, CFG-4).</summary>
    public string Repo { get; init; } = "";

    /// <summary>The config file that was read (CFG-2).</summary>
    public string ConfigPath { get; init; } = "";

    /// <summary>A <c>-Config</c> file, used as it is and never re-read from the remote (CFG-3).</summary>
    public bool ExplicitConfig { get; init; }

    /// <summary>The config came from <c>&lt;remote&gt;/&lt;branch&gt;</c> rather than the checkout (CFG-3).</summary>
    public bool FromRemote { get; init; }

    /// <summary>The <c>&lt;remote&gt;/&lt;branch&gt;</c> the config was re-read from, or attempted (CFG-3).</summary>
    public string RemoteRef { get; init; } = "";

    /// <summary>The JSON the config was built from.</summary>
    public string ConfigText { get; init; } = "";

    /// <summary>The configuration, with the command line applied; null when start-up failed.</summary>
    public SchedulerConfig? Config { get; init; }

    /// <summary>What the loader logged, in order.</summary>
    public IReadOnlyList<string> LogLines { get; init; } = [];
}
