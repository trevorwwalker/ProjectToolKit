// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>One worker window (PAGE-5): a slot and what it is doing.</summary>
public sealed record PageSlotView
{
    /// <summary>The slot number, from 1.</summary>
    public required int Slot { get; init; }

    /// <summary>The name of the worker type running in the slot, or empty when idle or untyped (WT-P1, WT-13).</summary>
    public string Type { get; init; } = "";

    /// <summary>Whether the slot is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The worker name from workers.json, or empty.</summary>
    public string WorkerName { get; init; } = "";

    /// <summary>The model, or empty.</summary>
    public string Model { get; init; } = "";

    /// <summary>The categories the slot takes.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The card it runs, or empty when idle.</summary>
    public string Card { get; init; } = "";

    /// <summary>When the card was started, or null when idle.</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>The last rendered log lines (PAGE-3, PAGE-10).</summary>
    public IReadOnlyList<string> LogTail { get; init; } = [];

    /// <summary>The stats line (PAGE-10).</summary>
    public string Stats { get; init; } = "";

    /// <summary>The stop reason text when it is being stopped, else empty.</summary>
    public string StopWhy { get; init; } = "";

    /// <summary>The minutes its running card may take (its time box, as status.json gives it), or null when idle (510-B).</summary>
    public int? TimeLimit { get; init; }

    /// <summary>When a stopping worker is killed if it has not ended, or null (510-B).</summary>
    public DateTimeOffset? KillAt { get; init; }

    /// <summary>The model's short name from models.json, or empty (510-B).</summary>
    public string ModelShort { get; init; } = "";

    /// <summary>The running card's title, or empty (510-B).</summary>
    public string CardTitle { get; init; } = "";

    /// <summary>The name of the running card's category in categories.json, or empty (510-B).</summary>
    public string CategoryName { get; init; } = "";

    /// <summary>Minutes since a running worker's log last grew, once it is 5 or more (TIME-8), else null (510-B).</summary>
    public int? QuietMinutes { get; init; }

    /// <summary>Whether an idle worker's model server does not answer (510-B).</summary>
    public bool ServerDown { get; init; }

    /// <summary>Whether a message sent from the page is waiting for the running worker to read it (510-B).</summary>
    public bool MessagePending { get; init; }

    /// <summary>
    /// When the running worker last had a message, from the stamp the hook puts on
    /// <c>msg-slotN.read.txt</c> as it hands the text over (WRK-14, job card 700-B); null when it has
    /// had none since it started being tracked.
    /// </summary>
    public DateTimeOffset? MessageReadAt { get; init; }

    /// <summary>Dynamic locks (DL-24, DL-25): the card's held set and open wait; null when locks are off or it holds nothing.</summary>
    public PageHolds? Holds { get; init; }
}
