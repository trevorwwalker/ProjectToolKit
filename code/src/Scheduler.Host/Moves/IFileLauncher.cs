// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Moves;

/// <summary>Opens a file with the program this PC opens it with (MOVE-9: the .md handler).</summary>
public interface IFileLauncher
{
    /// <summary>Open <paramref name="file"/>; throws when it cannot.</summary>
    void Open(string file);
}
