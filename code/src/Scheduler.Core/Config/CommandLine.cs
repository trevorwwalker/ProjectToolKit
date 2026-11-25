// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Config;

/// <summary>
/// The scheduler's own command line (CFG-1, CFG-7): the switches, their values, and the first
/// positional argument as the repository. What was given is what overrides the config for this run.
/// </summary>
public static class CommandLine
{
    private static readonly string[] SwitchesWithValues = ["-Repo", "-Config", "-Slots", "-Port", "-Model"];

    private static readonly string[] Flags = ["-NoHealthCheck", "-Paused", "-DryRun"];

    /// <summary>
    /// Read the arguments. Switch names match as PowerShell matches them, ignoring case, and a
    /// switch may carry its value with a colon or an equals sign (<c>-Slots:4</c>) or in the next
    /// argument. A number that is not a number, or a zero, counts as not given (CFG-7).
    /// </summary>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var repo = "";
        var configPath = "";
        var model = "";
        var slots = 0;
        var port = 0;
        var noHealthCheck = false;
        var paused = false;
        var dryRun = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Length == 0 || arg[0] != '-')
            {
                // CFG-1: the first positional argument is the repository.
                if (repo.Length == 0)
                {
                    repo = arg;
                }

                continue;
            }

            var name = arg;
            string? inlineValue = null;
            var split = arg.IndexOfAny([':', '=']);
            if (split > 1)
            {
                name = arg[..split];
                inlineValue = arg[(split + 1)..];
            }

            // The value of -Repo, -Config, -Slots, -Port and -Model is the next argument.
            string Value()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                i++;
                return i < args.Count ? args[i] : "";
            }

            if (Is(name, "-NoHealthCheck"))
            {
                noHealthCheck = true;
            }
            else if (Is(name, "-Paused"))
            {
                paused = true;
            }
            else if (Is(name, "-DryRun"))
            {
                dryRun = true;
            }
            else if (Is(name, "-Repo"))
            {
                repo = Value();
            }
            else if (Is(name, "-Config"))
            {
                configPath = Value();
            }
            else if (Is(name, "-Model"))
            {
                model = Value();
            }
            else if (Is(name, "-Slots"))
            {
                slots = Number(Value());
            }
            else if (Is(name, "-Port"))
            {
                port = Number(Value());
            }

            // Any other switch is unbound, as an argument PowerShell had no parameter for is.
        }

        return new CommandLineOptions
        {
            Repo = repo,
            ConfigPath = configPath,
            Slots = slots,
            Port = port,
            Model = model,
            NoHealthCheck = noHealthCheck,
            Paused = paused,
            DryRun = dryRun,
        };
    }

    private static bool Is(string name, string switchName) => Text.Same(name, switchName);

    /// <summary>The number, or 0 - which is how PowerShell read an absent <c>-Slots</c>/<c>-Port</c>.</summary>
    private static int Number(string text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
