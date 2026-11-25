// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Locks;

/// <summary>
/// One path a pushed range or a commit on main changes (design <c>dynamic-locks.md</c>, DL-16, DL-18, DL-19),
/// as Host reads it from git (<c>--numstat</c>, <c>--name-status</c>).
/// </summary>
/// <param name="Path">The path, relative to the repository; backslashes are read as slashes.</param>
/// <param name="New">True when the range adds the file (DL-15 c: new files under <c>jobs/requests/</c>).</param>
/// <param name="Added">Lines added.</param>
/// <param name="Deleted">Lines deleted (DL-16: an add-only file that deletes lines is reported).</param>
public sealed record PushCheckPath(string Path, bool New = false, int Added = 0, int Deleted = 0);
