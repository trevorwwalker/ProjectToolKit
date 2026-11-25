// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using StartupBench;

// 1032-B: times a pool start of the built scheduler on a generated pool and prints the medians as JSON.
//   StartupBench --exe <scheduler-next.exe> [--out <file>] [--runs 3] [--projects 3] [--cards 300] [--big 2000]
var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i + 1 < args.Length; i += 2)
{
    opts[args[i].TrimStart('-')] = args[i + 1];
}

if (!opts.TryGetValue("exe", out var exe) || !File.Exists(exe))
{
    Console.Error.WriteLine("usage: StartupBench --exe <scheduler-next.exe> [--out <file>] [--runs 3] [--projects 3] [--cards 300] [--big 2000]");
    return 2;
}

int Opt(string name, int fallback) => opts.TryGetValue(name, out var v) && int.TryParse(v, out var n) ? n : fallback;

var runs = Opt("runs", 3);
// Under out	est-tmp in the repository (inside the Defender exclusion), like the unit tests' folders (1042-B);
// %TEMP% only when the repository root is not found above the program.
var scratch = Path.GetTempPath();
for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
{
    if (File.Exists(Path.Combine(d.FullName, "code", "src", "SchedulerNext.slnx")))
    {
        scratch = Path.Combine(d.FullName, "out", "test-tmp");
        Directory.CreateDirectory(scratch);
        break;
    }
}

var root = Path.Combine(scratch, "ptk-startup-bench-" + Guid.NewGuid().ToString("N")[..8]);
try
{
    var build = Stopwatch.StartNew();
    var pool = PoolData.Create(root, Opt("projects", 3), Opt("cards", 300), Opt("big", 2000), FreePort());
    Console.Error.WriteLine($"bench: pool of {pool.ProjectNames.Count} projects written in {build.Elapsed.TotalSeconds:0.0}s under {root}");

    var page = new List<double>();
    var all = new List<double>();
    for (var run = 1; run <= runs; run++)
    {
        var (p, a) = TimeOneStart(exe, pool, Opt("timeout", 100));
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: run {run}: page {p:0.00}s, every project joined {a:0.00}s"));
        page.Add(p);
        all.Add(a);
    }

    string List(List<double> xs) => string.Join(",", xs.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)));
    var json = string.Create(CultureInfo.InvariantCulture,
        $"{{\"pageSeconds\":{Median(page):0.00},\"startSeconds\":{Median(all):0.00},\"runs\":{runs},\"pageRuns\":[{List(page)}],\"startRuns\":[{List(all)}]}}");
    if (opts.TryGetValue("out", out var outFile))
    {
        File.WriteAllText(outFile, json);
    }

    Console.WriteLine(json);
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine("bench failed: " + e.Message);
    return 1;
}
finally
{
    try
    {
        if (!opts.ContainsKey("keep") && Directory.Exists(root))
        {
            PoolData.Delete(root);
        }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"bench: could not delete {root} ({e.Message})");
    }
}

static double Median(List<double> xs)
{
    var s = xs.Order().ToList();
    return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
}

static int FreePort()
{
    var l = new TcpListener(IPAddress.Loopback, 0);
    l.Start();
    var port = ((IPEndPoint)l.LocalEndpoint).Port;
    l.Stop();
    return port;
}

// One start: process start to the page answering, and to the pool's "start took" line (every project joined and
// its first snapshot published, 1015-B). Then the process and its children are killed.
static (double Page, double All) TimeOneStart(string exe, PoolData pool, int timeout)
{
    var port = FreePort();
    pool.SetPort(port);
    var psi = new ProcessStartInfo(exe)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    var poolName = "-PoolName:ptkperf-" + Guid.NewGuid().ToString("N")[..8];
    foreach (var a in new[] { "-Pool", pool.PoolFile, "-Paused", "-NoBrowser", "-NoHealthCheck", poolName, "-Port", port.ToString(CultureInfo.InvariantCulture) })
    {
        psi.ArgumentList.Add(a);
    }

    var watch = Stopwatch.StartNew();
    using var proc = Process.Start(psi) ?? throw new InvalidOperationException("the scheduler did not start");
    var allAt = -1.0;
    var lastLine = "";
    proc.OutputDataReceived += (_, e) =>
    {
        if (e.Data is null)
        {
            return;
        }

        lastLine = ((lastLine + " | " + e.Data) is var t && t.Length > 1500 ? t[^1500..] : t);
        if (allAt < 0 && e.Data.Contains("pool: start took", StringComparison.Ordinal))
        {
            allAt = watch.Elapsed.TotalSeconds;
        }
    };
    proc.ErrorDataReceived += (_, e) =>
    {
        if (e.Data is not null)
        {
            lastLine = ((lastLine + " | " + e.Data) is var t && t.Length > 1500 ? t[^1500..] : t);
        }
    };
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var pageAt = -1.0;
        while (watch.Elapsed < TimeSpan.FromSeconds(timeout) && (pageAt < 0 || allAt < 0))
        {
            if (proc.HasExited)
            {
                throw new InvalidOperationException($"the scheduler ended with exit code {proc.ExitCode} (last line: {lastLine})");
            }

            if (pageAt < 0)
            {
                try
                {
                    using var r = http.GetAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
                    if (r.IsSuccessStatusCode)
                    {
                        pageAt = watch.Elapsed.TotalSeconds;
                    }
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    // not listening yet
                }
            }

            Thread.Sleep(10);
        }

        if (pageAt < 0 || allAt < 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"no result within the time limit (page {pageAt:0.0}, start {allAt:0.0}; last line: {lastLine})"));
        }

        return (pageAt, allAt);
    }
    finally
    {
        try
        {
            proc.Kill(true);
            proc.WaitForExit(10000);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
    }
}
