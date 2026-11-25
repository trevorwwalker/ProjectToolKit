// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Program;

/// <summary>The page's listener as the program sees it; <c>Scheduler.Web</c>'s PageServer is one.</summary>
public interface IPageListener : IDisposable
{
    /// <summary>Bind and start; throws <see cref="System.Net.Sockets.SocketException"/> when it cannot.</summary>
    void Start();

    /// <summary>The port it listens on.</summary>
    int Port { get; }

    /// <summary>Stop listening.</summary>
    void Stop();
}
