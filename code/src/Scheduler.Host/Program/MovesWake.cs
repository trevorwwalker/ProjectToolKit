// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Locks;
using Scheduler.Core.Tick;

namespace Scheduler.Host.Program;

/// <summary>
/// 966-B, 1020-B: which wake a file in the moves folder gives. A lock request (<c>.lock</c>) waiting there
/// wakes <see cref="TickWake.LockRequest"/> (design <c>dynamic-locks.md</c>, section 6), so the lock step
/// answers it in seconds and main is fetched as on any non-click wake; anything else is a move request.
/// </summary>
public static class MovesWake
{
    /// <summary>The wake for a change in <paramref name="folder"/>.</summary>
    public static TickWake Kind(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*" + LockRequests.Extension).Any()
                ? TickWake.LockRequest
                : TickWake.MoveRequest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TickWake.MoveRequest;
        }
    }
}
