using System.Net.Sockets;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Program;

/// <summary>Starting the page listener (CFG-17, L4013-4033).</summary>
public static class PageListening
{
    /// <summary>
    /// <c>lan</c> binds every address (the spec's <c>http://+:port/</c>); when that fails the cause is
    /// logged and the page is served on localhost; failing on localhost too is fatal.
    /// </summary>
    /// <param name="create">Makes a listener for a bind address (<c>0.0.0.0</c> or <c>127.0.0.1</c>) and port.</param>
    public static IPageListener Start(SchedulerConfig config, Func<string, int, IPageListener> create, ISchedulerLog log)
    {
        if (string.Equals(config.Listen, "lan", StringComparison.OrdinalIgnoreCase))
        {
            var lan = create("0.0.0.0", config.Port);
            try
            {
                lan.Start();
                return lan;
            }
            catch (SocketException e)
            {
                lan.Dispose();
                log.Write($"cannot listen on the network (http://+:{config.Port}/): {Reason(e, config.Port)}; serving localhost only");
            }
        }

        var local = create("127.0.0.1", config.Port);
        try
        {
            local.Start();
            return local;
        }
        catch (SocketException e)
        {
            local.Dispose();
            throw new SchedulerStartException($"cannot listen on http://localhost:{config.Port}/: {Reason(e, config.Port)}");
        }
    }

    private static string Reason(SocketException e, int port) => e.SocketErrorCode switch
    {
        SocketError.AddressAlreadyInUse => $"port {port} is already in use by another program",
        SocketError.AccessDenied =>
            $"no permission (missing URL reservation); as administrator run: netsh http add urlacl url=http://+:{port}/ user=Everyone",
        _ => e.Message,
    };
}
