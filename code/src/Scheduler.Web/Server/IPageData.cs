// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// What the host gives the page that is not a POST: the latest snapshot the page thread serves
/// (PAGE-1), the log folder the <c>/log</c> route reads, and the few texts that live in the
/// scheduler rather than in a snapshot. Host implements it; the Web tests fake it.
/// </summary>
public interface IPageData
{
    /// <summary>The newest snapshot, built on the scheduler thread.</summary>
    PageSnapshot Snapshot();

    /// <summary>The page font file <c>/font</c> serves, or null when none is configured.</summary>
    string? FontPath { get; }

    /// <summary>The log folder. No <c>/log</c> request may read outside it.</summary>
    string LogDirectory { get; }

    /// <summary>
    /// The text of a card for <c>/card?name=</c>, or null when there is no such card. <paramref name="name"/> is the
    /// file name without <c>.md</c> or the card's short id (<c>1091-C</c>); the card may be in any folder, done/ and
    /// archive/ included (card 1113-C).
    /// </summary>
    string? Card(string name);

    /// <summary>The text of a request for <c>/request?name=</c>, or null when there is none.</summary>
    string? Request(string name);

    /// <summary>The health log for <c>/health</c>, or null when no health check has run.</summary>
    string? HealthText();

    /// <summary>The JSON <c>/status.json</c> answers with.</summary>
    string StatusJson();

    /// <summary>
    /// The estimates report as last built, for <c>/estimates.json</c> (EST-31): this project's, or for a pool's data the pool's
    /// own. Null when none has been built yet, or this data has no estimates.
    /// </summary>
    string? EstimatesJson() => null;

    /// <summary>The workers that are running, for the live view (PAGE-3).</summary>
    IReadOnlyList<LiveWorker> RunningWorkers();

    /// <summary>The review panel for <c>/review?card=</c>, or null when there is no such card (roadmap item 6, card 650-C).</summary>
    ReviewPanel? Review(string card) => null;

    /// <summary>
    /// The path on disk of a repository file the review panel links to, or null when there is none or
    /// the name leaves the repository (roadmap item 6, card 650-C).
    /// </summary>
    string? RepoFile(string path) => null;

    /// <summary>
    /// The text of the plan document at <paramref name="path"/> (relative to the repository) on main, or null when
    /// it is not there, for <c>/plan</c> (card 1113-C).
    /// </summary>
    string? PlanText(string path) => null;
}
