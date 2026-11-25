// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Moves;

/// <summary>
/// The developer's uncommitted edit to a card (Local-Edits L1168): where it was found, the patch,
/// and the text it only appends at the end ("" when it changes anything else).
/// </summary>
internal sealed record LocalEdit(string Dir, string Patch, string Added);
