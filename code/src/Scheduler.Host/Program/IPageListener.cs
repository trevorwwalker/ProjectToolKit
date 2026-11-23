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
