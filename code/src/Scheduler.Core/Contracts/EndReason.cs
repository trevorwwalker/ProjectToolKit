// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// A run's outcome when it has no <c>LANE-RESULT:</c> word (TIME-14 to TIME-18); the text is
/// what the log, accounting and page show.
/// </summary>
public sealed record EndReason
{
    /// <summary>The reason.</summary>
    public required EndReasonKind Kind { get; init; }

    /// <summary>The exit code, for <see cref="EndReasonKind.Crashed"/>.</summary>
    public int? ExitCode { get; init; }

    /// <summary>The reason as the script writes it.</summary>
    public override string ToString() => Kind switch
    {
        EndReasonKind.NoLog => "no log",
        EndReasonKind.NoResultLine => "no result line",
        EndReasonKind.OutOfQuota => "out of quota",
        EndReasonKind.ModelServerDown => "model server down",
        EndReasonKind.TimeLimit => "time limit",
        EndReasonKind.Stalled => "stalled",
        EndReasonKind.Killed => "killed",
        EndReasonKind.StoppedFromPage => "stopped from page",
        EndReasonKind.Crashed => $"crashed (exit {ExitCode})",
        _ => "ended early",
    };
}
