namespace Scheduler.Tray;

/// <summary>
/// What a folder swap did (967-B): whether the new build is in place, and the line for the log window.
/// </summary>
/// <param name="Ok">Whether the new build is now the one in the running folder.</param>
/// <param name="Message">The line that says what happened.</param>
public sealed record SwapOutcome(bool Ok, string Message);
