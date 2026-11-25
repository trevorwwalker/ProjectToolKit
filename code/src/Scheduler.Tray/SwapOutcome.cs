// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>
/// What a folder swap did (967-B): whether the new build is in place, and the line for the log window.
/// </summary>
/// <param name="Ok">Whether the new build is now the one in the running folder.</param>
/// <param name="Message">The line that says what happened.</param>
public sealed record SwapOutcome(bool Ok, string Message);
