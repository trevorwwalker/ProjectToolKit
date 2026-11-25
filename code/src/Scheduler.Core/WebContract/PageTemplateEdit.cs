// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>
/// One generic file whose copy in <paramref name="Project"/> was edited by hand, so the scheduler
/// lists it under Waiting on you instead of overwriting it (roadmap item 1, job card 580-A). Host's
/// <c>TemplateRefresh.Edited</c> is where these come from.
/// </summary>
/// <param name="Project">The project the edited copy belongs to.</param>
/// <param name="Target">The copy's path in that project, as <c>jobs/README.md</c>.</param>
public sealed record PageTemplateEdit(string Project, string Target);
