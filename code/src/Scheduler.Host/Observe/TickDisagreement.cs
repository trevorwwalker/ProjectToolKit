namespace Scheduler.Host.Observe;

/// <summary>One observe tick where the two schedulers disagree, and how (roadmap item 1).</summary>
/// <param name="Tick">The observe tick's number.</param>
/// <param name="Time">When the tick ended.</param>
/// <param name="Differences">
/// <c>old only: ...</c> and <c>new only: ...</c> for a start or finish only one made, and
/// <c>state: old X, new Y</c>.
/// </param>
public sealed record TickDisagreement(int Tick, DateTime Time, IReadOnlyList<string> Differences);
