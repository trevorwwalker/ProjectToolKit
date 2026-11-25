// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>Why a worker's run ended without a result line (TIME-14 to TIME-18).</summary>
public enum EndReasonKind
{
    /// <summary><c>no log</c>: the worker wrote no log file (TIME-14).</summary>
    NoLog,

    /// <summary><c>no result line</c> (TIME-14).</summary>
    NoResultLine,

    /// <summary><c>out of quota</c> (TIME-15).</summary>
    OutOfQuota,

    /// <summary><c>model server down</c> (TIME-16).</summary>
    ModelServerDown,

    /// <summary><c>time limit</c> (TIME-18).</summary>
    TimeLimit,

    /// <summary><c>stalled</c> (TIME-18).</summary>
    Stalled,

    /// <summary><c>killed</c> (TIME-18).</summary>
    Killed,

    /// <summary><c>stopped from page</c> (TIME-18).</summary>
    StoppedFromPage,

    /// <summary><c>crashed (exit N)</c> (TIME-18).</summary>
    Crashed,

    /// <summary><c>ended early</c> (TIME-18).</summary>
    EndedEarly,
}
