// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// The version of a page snapshot (PAGE-3, card 470-C): a short hash that changes when anything
/// the page shows changes, except what changes on every tick - the build time, the host load, and
/// each worker's log tail and stats line, which the page script updates in place from
/// <c>/live.json</c>. The page carries the version it was drawn from; <c>/live.json</c> carries
/// the current one, and when they differ the page refreshes itself.
/// </summary>
public static class PageVersion
{
    private static readonly JsonSerializerOptions Options = new();

    /// <summary>The version of one project's page.</summary>
    public static string Of(PageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Hash(JsonSerializer.Serialize(Steady(snapshot), Options));
    }

    /// <summary>The version of the combined page, from every project's snapshot in order.</summary>
    public static string Of(IReadOnlyList<PageSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return Hash(string.Join('|', snapshots.Where(snapshot => snapshot is not null).Select(Of)));
    }

    // The snapshot without what the live view keeps current by itself.
    private static PageSnapshot Steady(PageSnapshot snapshot) => snapshot with
    {
        Built = DateTimeOffset.UnixEpoch,
        HostLoad = null,
        Slots = [.. snapshot.Slots.Select(slot => slot with { LogTail = [], Stats = "" })],
    };

    private static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8);
}
