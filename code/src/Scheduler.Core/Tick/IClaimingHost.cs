// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tick;

/// <summary>995-B: what a claim handed off the tick has come to.</summary>
public enum ClaimState
{
    /// <summary>Not on main yet.</summary>
    Pending,

    /// <summary>On main: the card is in active/ and its worker may start (CM-9).</summary>
    Landed,

    /// <summary>Refused or lost (someone else moved the card): the slot is freed.</summary>
    Failed,
}

/// <summary>
/// 995-B: a tick host that pushes the claim at dispatch (<c>jobs: start</c>, CM-9) off the tick. The
/// tick hands it the claim and holds the slot as starting; at each later tick it asks how the claim
/// went, and starts the worker with <see cref="ITickHost.StartWorker(int, Card, bool, string)"/> only
/// once the claim is on main. A host without it claims inside <c>StartWorker</c>, as before.
/// </summary>
public interface IClaimingHost
{
    /// <summary>
    /// Hands the claim of <paramref name="card"/> for <paramref name="slot"/> to the writer and returns at
    /// once. False when no claim is posted (a resume, a card not in queue/, no writer): the tick then
    /// starts the worker at once.
    /// </summary>
    bool PostClaim(int slot, Card card, bool resume);

    /// <summary>The claim posted for <paramref name="slot"/>; a landed or failed one is answered once.</summary>
    ClaimState ClaimOf(int slot);
}
