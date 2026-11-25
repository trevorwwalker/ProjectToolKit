// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>One file a commit changed, with its line counts (roadmap item 6, card 650-C).</summary>
public sealed record ReviewFile(string Path, int Added, int Removed);
