// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Config;

/// <summary>
/// What the scheduler was given on the command line (CFG-7). Everything here is what one run was
/// told; the file keeps its own values. A switch that was not given, or was given 0, is PowerShell's
/// "not given" and does not override the config.
/// </summary>
public sealed record CommandLineOptions
{
    /// <summary>Nothing was given: every value comes from the config file.</summary>
    public static CommandLineOptions None { get; } = new();

    /// <summary><c>-Repo</c>, or the first positional argument (CFG-1). "" when neither was given.</summary>
    public string Repo { get; init; } = "";

    /// <summary><c>-Config</c>: the file to use as it is, never re-read from main (CFG-2, CFG-3).</summary>
    public string ConfigPath { get; init; } = "";

    /// <summary><c>-Slots</c>; 0 means it was not given (CFG-7).</summary>
    public int Slots { get; init; }

    /// <summary><c>-Port</c>; 0 means it was not given (CFG-7).</summary>
    public int Port { get; init; }

    /// <summary><c>-Model</c>; "" means it was not given (CFG-7).</summary>
    public string Model { get; init; } = "";

    /// <summary><c>-NoHealthCheck</c>: the health command is emptied (CFG-7).</summary>
    public bool NoHealthCheck { get; init; }

    /// <summary><c>-Paused</c>: the scheduler starts in state paused (CFG-7).</summary>
    public bool Paused { get; init; }

    /// <summary><c>-DryRun</c> (CFG-7, DISP-21).</summary>
    public bool DryRun { get; init; }

    /// <summary><c>-Slots</c> with a count in it: the worker count is then pinned (CFG-8).</summary>
    public bool SlotsGiven => Slots != 0;
}
