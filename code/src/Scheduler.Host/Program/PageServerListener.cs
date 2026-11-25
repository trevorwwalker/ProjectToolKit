// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Web.Server;

namespace Scheduler.Host.Program;

/// <summary><see cref="IPageListener"/> over Web's <see cref="PageServer"/>.</summary>
public sealed class PageServerListener(PageServer server) : IPageListener
{
    /// <inheritdoc />
    public int Port => server.Port;

    /// <inheritdoc />
    public void Start() => server.Start();

    /// <inheritdoc />
    public void Stop() => server.Stop();

    /// <inheritdoc />
    public void Dispose() => server.Dispose();

    /// <summary>
    /// A listener factory for <see cref="PageListening.Start"/>: the same page options on the given address and port.
    /// 1110-B: <paramref name="live"/> is the hub <c>/live/stream</c> reads its frames from; null leaves the route
    /// refused (a one-project scheduler stays on the 2 second poll).
    /// </summary>
    public static Func<string, int, IPageListener> Factory(Func<string, int, PageServerOptions> options, LiveStreamSource? live = null) =>
        (bind, port) => new PageServerListener(new PageServer(options(bind, port)) { LiveSource = live });
}
