// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Dispatch;

/// <summary>Why a card is not a candidate this tick, for the Queue window (DISP-4 to DISP-13).</summary>
/// <param name="Card">The card's name (file name without <c>.md</c>).</param>
/// <param name="Note">The note, worded exactly as schedule.ps1 words it.</param>
public sealed record DispatchNote(string Card, string Note);
