using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>How to build a <see cref="PageServer"/>.</summary>
public sealed class PageServerOptions
{
    /// <summary>The port to listen on; 0 takes any free one (never the scheduler's own 8765).</summary>
    public int Port { get; init; }

    /// <summary>The address to bind. Loopback is enough until remoteControl is on.</summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>Whether another PC may use the controls (PAGE-7).</summary>
    public bool RemoteControl { get; init; }

    /// <summary>The snapshot and the host's texts.</summary>
    public required IPageData Data { get; init; }

    /// <summary>Every POST route, as the contract has them.</summary>
    public required IPageActions Actions { get; init; }

    /// <summary>The page itself, built by the views area.</summary>
    public required IPageRenderer Renderer { get; init; }

    /// <summary>The live view of PAGE-3.</summary>
    public required LiveFeed Feed { get; init; }

    /// <summary>
    /// The one background worker the POST actions run on (957-B). Whoever builds the page server and
    /// the live feed shares one queue between them, so the feed can tell the page how far each click
    /// got; a server with no queue of its own makes one and disposes it.
    /// </summary>
    public PageActionQueue? Queue { get; init; }

    /// <summary>A pool's data, actions and page (MP-P8); null serves one project as today (MP-C9).</summary>
    public PoolRouting? Pool { get; init; }
}
