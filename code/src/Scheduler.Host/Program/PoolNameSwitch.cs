using Scheduler.Core.Contracts;

namespace Scheduler.Host.Program;

/// <summary>
/// <c>-PoolName:&lt;name&gt;</c> (1032-B): the name the pool's single-instance mutex is made from (MP-H1),
/// <c>pool</c> when the switch is absent. The startup benchmark gives its own pool another name so it can
/// run while the live pool does. Like <c>-Replaces</c> it carries its value in the same argument and is read
/// at the entry point, because the scheduler's command line leaves a switch it does not bind alone (CFG-7).
/// </summary>
public static class PoolNameSwitch
{
    /// <summary>The switch as it is typed.</summary>
    public const string SwitchName = "-PoolName";

    /// <summary>The default name, the one every pool has without the switch.</summary>
    public const string DefaultName = "pool";

    /// <summary>The pool's name: the switch's value, or <see cref="DefaultName"/>.</summary>
    public static string Name(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            var split = arg.IndexOfAny([':', '=']);
            if (split > 1 && Text.Same(arg[..split], SwitchName) && !string.IsNullOrWhiteSpace(arg[(split + 1)..]))
            {
                return arg[(split + 1)..].Trim();
            }
        }

        return DefaultName;
    }
}
