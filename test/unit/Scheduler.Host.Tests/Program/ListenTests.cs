// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Net.Sockets;
using Scheduler.Core.Contracts;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Program;

public class ListenTests
{
    /// <summary>A listener that binds for real, or fails the way the test says.</summary>
    private sealed class TestListener : IPageListener
    {
        private readonly string _bind;
        private readonly int _requested;
        private readonly SocketError? _failWith;
        private TcpListener? _tcp;

        public TestListener(string bind, int requested, SocketError? failWith)
        {
            _bind = bind;
            _requested = requested;
            _failWith = failWith;
            Port = requested;
        }

        public int Port { get; private set; }

        public void Start()
        {
            if (_failWith is { } error)
            {
                throw new SocketException((int)error);
            }

            _tcp = new TcpListener(IPAddress.Parse(_bind), _requested);
            _tcp.Start();
            Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
        }

        public void Stop() => _tcp?.Stop();

        public void Dispose() => Stop();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // CFG-17: listen lan, the + prefix fails (port taken, or URL reservation missing): the log names the
    // cause (and the netsh command for the reservation) and the page is served on localhost.
    [Theory]
    [InlineData(SocketError.AddressAlreadyInUse, "already in use", false)]
    [InlineData(SocketError.AccessDenied, "netsh http add urlacl url=http://+:", true)]
    public void Config_ListenLan_FallsBackToLocalhostWithReason(SocketError cause, string expectedText, bool netsh)
    {
        var port = FreePort();
        var log = new ListLog();
        var binds = new List<string>();

        using var listener = PageListening.Start(
            new SchedulerConfig { Listen = "lan", Port = port },
            (bind, p) =>
            {
                binds.Add(bind);
                return new TestListener(bind, p, bind == "0.0.0.0" ? cause : null);
            },
            log);

        Assert.Equal(["0.0.0.0", "127.0.0.1"], binds);
        var line = Assert.Single(log.Lines);
        Assert.Contains(expectedText, line);
        Assert.Equal(netsh, line.Contains("netsh http add urlacl"));
        Assert.Contains($"{port}", line);
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, listener.Port); // served on localhost
    }

    // CFG-17: the port is held by another listener on localhost: start-up fails.
    [Fact]
    public void Config_ListenLocalhostFails_IsFatal()
    {
        var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        try
        {
            var port = ((IPEndPoint)holder.LocalEndpoint).Port;

            var e = Assert.Throws<SchedulerStartException>(() => PageListening.Start(
                new SchedulerConfig { Listen = "localhost", Port = port },
                (bind, p) => new TestListener(bind, p, null),
                new ListLog()));

            Assert.Contains($"{port}", e.Message);
            Assert.Contains("already in use", e.Message);
        }
        finally
        {
            holder.Stop();
        }
    }
}
