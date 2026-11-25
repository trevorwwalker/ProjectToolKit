// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Web.Views;

/// <summary>One theme as the appearance selector and the page's head need it (PAGE-11).</summary>
/// <param name="Key">The name the style block is carried under; the browser keeps this.</param>
/// <param name="Label">The name in the selector.</param>
/// <param name="Css">Its CSS, with the CSS of the theme it extends in front.</param>
public sealed record PageThemeEntry(string Key, string Label, string Css);
