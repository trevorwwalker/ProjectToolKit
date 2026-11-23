using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// The page data of a pool (multi-project design section 6, Web): what <see cref="IPageData"/> gives
/// for the pool as a whole - its log folder, status, and every project's running workers keyed by
/// project and slot (WT-W3), which the live feed reads unchanged - plus the pool's own snapshot and
/// each project's own data, which the per-project routes of MP-P8 answer from. Host implements it.
/// </summary>
public interface IPoolPageData : IPageData
{
    /// <summary>The newest pool snapshot: every project's page plus the pool part (section 4).</summary>
    PoolPageSnapshot PoolSnapshot();

    /// <summary>The data of <paramref name="project"/> (names compare without case), or null when it is not in the pool.</summary>
    IPageData? For(string project);
}
