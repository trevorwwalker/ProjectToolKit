using Scheduler.Core.Dispatch;

namespace Scheduler.Core.Pool;

/// <summary>The card a project would start now, and the type that takes it (MP-5, MP-7, WT-7).</summary>
/// <param name="Project">The project's name.</param>
/// <param name="Candidate">The card, and whether it is an orphan to resume.</param>
public sealed record PoolOffer(string Project, DispatchCandidate Candidate)
{
    /// <summary>The name of the type WT-5 gives the card.</summary>
    public string Type { get; init; } = "";
}
