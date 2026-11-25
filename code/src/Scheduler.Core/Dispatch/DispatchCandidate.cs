// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Dispatch;

/// <summary>A card that may start this tick (DISP-8, DISP-13); <paramref name="Resume"/> for an orphan in active/.</summary>
/// <param name="Card">The card.</param>
/// <param name="Resume">It is an orphan to resume, not a new card from queue/.</param>
public sealed record DispatchCandidate(Card Card, bool Resume);
