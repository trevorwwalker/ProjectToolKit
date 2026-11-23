using System.Text.RegularExpressions;
using Scheduler.Core;
using Scheduler.Host.PageActions;
using Scheduler.Host.Program;
using Scheduler.Web.Server;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// CFG-18: the version the page and the log show is the build's own, Major.Minor.Rev.
/// test-plans/config.md; started through the real runner (270-B) and read from the real page (050-C).
/// </summary>
public class VersionPageTests
{
    [Fact]
    public async Task Config_Version_ShownOnPageAndLog()
    {
        // 951-A: the one place the number is written is Directory.Build.props, and the page and the
        // log show what CoreInfo read back out of this build.
        var version = SchedulerVersion.Text;
        Assert.Equal(CoreInfo.Version, version);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), version);
        var assembly = typeof(HostPageActions).Assembly.GetName().Version
            ?? throw new InvalidOperationException("the Host assembly has no version");
        // A three-part <Version> sits in the assembly's four-part version as Major.Minor.Build, so
        // the Rev the build carries is Build, not Revision (950-C).
        Assert.Equal($"{assembly.Major}.{assembly.Minor}.{assembly.Build}", version);

        using var harness = new PageActionHarness(project: "ptk-280b-cfg18");
        var config = harness.Config with { Project = "ptk-280b-cfg18", Port = 0, Listen = "localhost" };
        IPageListener? listener = null;
        var parts = new RunnerParts
        {
            Config = config,
            Clock = harness.Clock,
            Log = harness.Log,
            Version = version,
            AdoptWorkers = () => { },
            SeedFromAccounting = () => { },
            StartListener = () =>
            {
                var started = PageListening.Start(
                    config,
                    PageServerListener.Factory((bind, port) => new PageServerOptions
                    {
                        BindAddress = bind,
                        Port = port,
                        RemoteControl = false,
                        Data = harness.Data,
                        Actions = harness.Actions,
                        Renderer = harness.Renderer,
                        Feed = harness.Feed,
                    }),
                    harness.Log);
                listener = started;
                return started;
            },
            OpenBrowser = _ => { },
            Tick = _ => { },
            Draining = () => false,
            WorkersRunning = () => 0,
            WriteStatusFile = () => { },
            WaitForTick = (wait, stop) => stop.WaitHandle.WaitOne(wait),
        };

        using var stop = new CancellationTokenSource();
        var runner = Task.Run(() => new SchedulerRunner(parts).Run(stop.Token));
        var opened = await Spin.WaitUntil(() => listener?.Port > 0 ? listener!.Port : 0, 15000);
        Assert.True(opened > 0, "the page never started listening");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var page = await client.GetStringAsync($"http://127.0.0.1:{opened}/");
            Assert.Contains(version, page);

            var start = Assert.Single(harness.Log.Lines, line => line.Contains("started for"));
            Assert.Contains(version, start);
        }
        finally
        {
            stop.Cancel();
            var finished = await Task.WhenAny(runner, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.Same(runner, finished);
        }
    }
}

/// <summary>Waits for a condition the way these tests need it: a short poll, bounded by a limit.</summary>
internal static class Spin
{
    public static async Task<int> WaitUntil(Func<int> value, int limitMs)
    {
        var deadline = Environment.TickCount64 + limitMs;
        while (Environment.TickCount64 < deadline)
        {
            var got = value();
            if (got != 0)
            {
                return got;
            }

            await Task.Delay(25);
        }

        return 0;
    }
}
