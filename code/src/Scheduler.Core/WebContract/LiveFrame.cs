// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>
/// 1101-B: one frame of the live view at up to 24 a second, holding only what changed since the frame before it.
/// A client keeps the highest <see cref="Seq"/> it has seen; a frame is never built while nothing changed.
/// </summary>
public sealed record LiveFrame
{
    /// <summary>The frame's number, rising by one with every frame the hub builds.</summary>
    public required long Seq { get; init; }

    /// <summary>When the frame was built.</summary>
    public required DateTimeOffset T { get; init; }

    /// <summary>The slots that changed, keyed as the live JSON keys them (<c>4</c>, or <c>Project:4</c> in a pool).</summary>
    public IReadOnlyDictionary<string, LiveFrameSlot> Slots { get; init; } = new Dictionary<string, LiveFrameSlot>();

    /// <summary>The CPU and memory, when the shown value changed; otherwise null.</summary>
    public LiveFrameLoad? Load { get; init; }

    /// <summary>The page snapshot's version, when it moved (the page redraws its structure); otherwise null.</summary>
    public string? Page { get; init; }
}

/// <summary>What changed in one slot since the previous frame; a null part did not change.</summary>
public sealed record LiveFrameSlot
{
    /// <summary>True when the run or card changed: the client drops the slot's lines before adding <see cref="Lines"/>.</summary>
    public bool Reset { get; init; }

    /// <summary>The rendered log lines added since the previous frame, in order.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>The stats line, when it changed.</summary>
    public string? Stats { get; init; }

    /// <summary>The minutes and time-limit text (<c>3/90 min, quiet 6 min</c>), when it changed.</summary>
    public string? Time { get; init; }
}

/// <summary>The host's load as shown: the latest and the smoothed CPU, and memory.</summary>
public sealed record LiveFrameLoad(int Cpu, int CpuSmooth, long FreeMemoryBytes, long TotalMemoryBytes);
