// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// The page itself, which the views area (090-C) builds. The server asks for a version rather
/// than building one, so a request from another PC gets the read-only page whose controls are
/// disabled with the reason (PAGE-7).
/// </summary>
public interface IPageRenderer
{
    /// <summary>
    /// The HTML for a snapshot.
    /// </summary>
    /// <param name="snapshot">What to show.</param>
    /// <param name="remote">Whether the reader is on another PC.</param>
    /// <param name="controlsAllowed">
    /// Whether the controls work for this reader: on this PC, or from anywhere with remoteControl.
    /// When false the page shows its buttons disabled with
    /// "(Only on the PC running the scheduler.)".
    /// </param>
    string Render(PageSnapshot snapshot, bool remote, bool controlsAllowed);
}
