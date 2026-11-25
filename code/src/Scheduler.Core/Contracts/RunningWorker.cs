// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// A worker running a card: what running.json keeps for it (ADOPT-1) and what the watchdog
/// reads (A6).
/// </summary>
public sealed record RunningWorker
{
    /// <summary>The slot number.</summary>
    public required int Slot { get; init; }

    /// <summary>The process id of the <c>cmd</c> that runs the agent (WRK-12).</summary>
    public required int Pid { get; init; }

    /// <summary>The process's start time; alive means within 2 s of it (TIME-13).</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>The card's name (file name without <c>.md</c>).</summary>
    public required string Card { get; init; }

    /// <summary>The card's category.</summary>
    public required string Category { get; init; }

    /// <summary>1030-A: the card's type (<c>feature</c>, <c>bug</c> or ""), for the accounting row.</summary>
    public string CardType { get; init; } = "";

    /// <summary>1030-A: the card's <c>Version:</c> header, for the accounting row.</summary>
    public string CardVersion { get; init; } = "";

    /// <summary>The card's id.</summary>
    public required string Id { get; init; }

    /// <summary>The readable log file (WRK-9).</summary>
    public string Log { get; init; } = "";

    /// <summary>The raw log file: the <c>.jsonl</c> for json formats, else the same as <see cref="Log"/> (WRK-9).</summary>
    public string RawLog { get; init; } = "";

    /// <summary>How far the raw log has been rendered.</summary>
    public long RawPosition { get; init; }

    /// <summary>The raw log's last size, for progress (TIME-3).</summary>
    public long LastSize { get; init; }

    /// <summary>When the raw log last changed size (TIME-8).</summary>
    public DateTimeOffset LastProgress { get; init; }

    /// <summary>The job object's name (WRK-13).</summary>
    public string Job { get; init; } = "";

    /// <summary>The card's Time box, or null for maxMinutes (TIME-1).</summary>
    public int? TimeBox { get; init; }

    /// <summary>It is a resume of an orphan (DISP-16, TIME-17).</summary>
    public bool Resume { get; init; }

    /// <summary>The warning file has been written (TIME-4).</summary>
    public bool Warned { get; init; }

    /// <summary>When a stop was requested, or null (TIME-9).</summary>
    public DateTimeOffset? StopAt { get; init; }

    /// <summary>Why it was asked to stop (TIME-9, TIME-18).</summary>
    public string StopWhy { get; init; } = "";

    /// <summary>Minutes it gets after the stop request (TIME-5, TIME-9).</summary>
    public int StopGraceMinutes { get; init; }

    /// <summary>When it was killed, or null (TIME-11).</summary>
    public DateTimeOffset? KillTime { get; init; }

    /// <summary>It was abandoned: still alive 2 min after a kill (TIME-11).</summary>
    public bool Abandoned { get; init; }

    /// <summary>The page may message it (WRK-14).</summary>
    public bool Msgs { get; init; }

    /// <summary>The agent's name.</summary>
    public string Agent { get; init; } = "";

    /// <summary>The effective model (WRK-4).</summary>
    public string Model { get; init; } = "";

    /// <summary>The effective endpoint (WRK-4).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>
    /// The name of the worker type it started with (WT-W5, WT-13); empty for an entry written before
    /// types, or one whose slot no type was migrated from. It counts toward the total always, and
    /// toward the cap of a type of that name while one exists.
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>The effective metrics URL (WRK-4).</summary>
    public string MetricsUrl { get; init; } = "";
}
