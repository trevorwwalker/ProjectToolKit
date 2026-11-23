using Scheduler.Core.Contracts;

namespace Scheduler.Core.Locks;

/// <summary>
/// A card's age for wait-die (design <c>dynamic-locks.md</c>, DL-11): high priority before normal
/// before low, then the lower card number. It is fixed the first time the card is claimed and kept in
/// the lock table, so a card that yields and returns keeps it even if its Priority line changes.
/// </summary>
/// <param name="Prio">0 high, 1 normal, 2 low (CARD-8).</param>
/// <param name="Num">The card's number.</param>
public readonly record struct CardAge(int Prio, long Num) : IComparable<CardAge>
{
    /// <summary>The age a card would get if it were claimed now.</summary>
    public static CardAge Of(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new(Math.Clamp(card.Prio, 0, 2), card.Num);
    }

    /// <summary>True when this card is strictly older than <paramref name="other"/>; two of the same age are neither.</summary>
    public bool IsOlderThan(CardAge other) => CompareTo(other) < 0;

    /// <summary>Older first.</summary>
    public int CompareTo(CardAge other) => Prio != other.Prio ? Prio.CompareTo(other.Prio) : Num.CompareTo(other.Num);
}
