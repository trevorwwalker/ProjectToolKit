namespace Scheduler.Core.Dispatch;

/// <summary>A start tried this tick (WT-6): the type chosen by WT-5, the candidate and whether it started.</summary>
/// <param name="Type">The name of the type that takes the card.</param>
/// <param name="Candidate">The card, and whether it is an orphan to resume.</param>
/// <param name="Started">The worker started; when false the card is still not tried again this tick.</param>
public sealed record TypeStart(string Type, DispatchCandidate Candidate, bool Started = true);
