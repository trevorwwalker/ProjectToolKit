// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;

namespace Scheduler.Core.Config;

/// <summary>
/// scheduler.json's dynamic-lock settings (design <c>dynamic-locks.md</c>, section 8 card 9; DL-13): the
/// <c>locks</c> switch, off until the developer's trial, and <c>limits.lockWaitMinutes</c>. With the
/// switch off, dispatch, prompts and worker environments are as they were before locks.
/// </summary>
public sealed record LockSettings
{
    /// <summary>The default wait limit for an older card (DL-13).</summary>
    public const int DefaultWaitMinutes = 20;

    /// <summary>Locks off: the default.</summary>
    public static LockSettings Off { get; } = new();

    /// <summary>The <c>locks</c> switch: true turns dynamic locks on.</summary>
    public bool On { get; init; }

    /// <summary><c>limits.lockWaitMinutes</c>: how long an older card waits for a lock (DL-13).</summary>
    public int WaitMinutes { get; init; } = DefaultWaitMinutes;

    /// <summary>
    /// The settings in <paramref name="jsonText"/> (scheduler.json). No file, text that is not JSON, or
    /// a <c>locks</c> that is not <c>true</c> leaves locks off; a wait limit below 1 is the default.
    /// </summary>
    public static LockSettings Load(string? jsonText)
    {
        try
        {
            var cfg = ConfigJson.Root(jsonText);
            var wait = ConfigJson.GetInt(ConfigJson.GetObject(cfg, "limits"), "lockWaitMinutes");
            return new LockSettings
            {
                On = Cfg.Value(ConfigJson.GetBool(cfg, "locks"), false),
                WaitMinutes = wait is > 0 ? wait.Value : DefaultWaitMinutes,
            };
        }
        catch (JsonException)
        {
            return Off;
        }
    }

    /// <summary>The arbiter's wait limit (<see cref="LockRules.WaitLimit"/>) for these settings.</summary>
    public TimeSpan WaitLimit => TimeSpan.FromMinutes(WaitMinutes);
}
