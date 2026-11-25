// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>The pool's page (section 4 of the multi-project design), drawn from one pool snapshot.</summary>
public interface IPoolPageRenderer
{
    /// <summary>The HTML for a pool snapshot; <paramref name="controlsAllowed"/> as <see cref="IPageRenderer.Render"/> has it.</summary>
    string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed);

    /// <summary>VW-2: the page in <paramref name="view"/> (the address's <c>view</c>); a renderer with one view ignores it.</summary>
    string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed, string? view) => Render(pool, remote, controlsAllowed);
}
