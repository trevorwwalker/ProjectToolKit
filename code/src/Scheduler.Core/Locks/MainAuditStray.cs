namespace Scheduler.Core.Locks;

/// <summary>
/// A stray (design <c>dynamic-locks.md</c>, DL-19): a change on main outside its card's held set, or a commit
/// with no <c>Card:</c> trailer while workers ran. Logged and shown; its card's finish goes to review.
/// </summary>
/// <param name="Sha">The commit.</param>
/// <param name="Card">The card its trailer names, or empty when it has none.</param>
/// <param name="Path">The path its card did not hold, or empty for a commit with no trailer.</param>
/// <param name="Reason">One line for the log and the page.</param>
public sealed record MainAuditStray(string Sha, string Card, string Path, string Reason);
