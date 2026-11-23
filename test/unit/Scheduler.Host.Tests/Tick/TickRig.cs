using System.Net;
using System.Net.Sockets;
using Scheduler.Core.Contracts;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Planner;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Tick;

/// <summary>The real clock plus a skew a test moves forward to pass minutes without waiting for them.</summary>
internal sealed class SkewClock : IClock
{
    public TimeSpan Skew { get; set; }

    /// <summary>When set, the clock reads this and stands still (<see cref="MoveTo"/>).</summary>
    public DateTimeOffset? Pinned { get; private set; }

    public DateTimeOffset Now => Pinned ?? DateTimeOffset.Now + Skew;

    /// <summary>
    /// 1051-B: stop the clock at <paramref name="at"/>. A test that places a step relative to an event (a
    /// worker's start) uses this, so the real minutes a busy PC spends getting there, or inside the tick,
    /// are not added on top.
    /// </summary>
    public void MoveTo(DateTimeOffset at) => Pinned = at;
}

/// <summary>
/// A temporary repository with a bare remote, one card in queue/, a fake agent CLI (a .cmd that
/// plays the worker) and the composed program over them. Never a real agent.
/// </summary>
internal sealed class TickRig : IDisposable
{
    public const string CardName = "010-B-x";
    public const string CardText = "# 010-B: x\n\nLane: B\nStatus: ready\n";
    private readonly TempDir _temp = new();

    public TickRig(
        string cliScript,
        string? plannerOutbox = null,
        bool dryRun = false,
        AgentFormat format = AgentFormat.Text,
        string? toolkit = null,
        string? workersJson = null,
        IReadOnlyDictionary<string, string>? cards = null,
        string? claudeCliScript = null,
        IReadOnlyDictionary<string, string>? workerEnv = null,
        IReadOnlyDictionary<string, string>? logFiles = null,
        int slots = 1,
        bool slotsPinned = true,
        bool locks = false)
    {
        Fx = new GitFixture();
        var seed = new Dictionary<string, string?>();
        foreach (var (path, text) in cards ?? new Dictionary<string, string> { [$"jobs/queue/{CardName}.md"] = CardText })
        {
            seed[path] = text;
        }

        if (workersJson is not null)
        {
            seed["jobs/workers.json"] = workersJson;
        }

        Fx.Push("seed", seed);
        // 1026-B: what `git config user.name scheduler`, `user.email s@s`, `commit.gpgsign false` and
        // `core.autocrlf false` set, written in one go rather than by four git processes.
        File.AppendAllText(Path.Combine(Fx.Repo, ".git", "config"), "[user]\n\tname = scheduler\n\temail = s@s\n[commit]\n\tgpgsign = false\n[core]\n\tautocrlf = false\n");
        GitFixture.Git(Fx.Repo, "pull", "-q", "origin", "main");

        LogDir = Path.Combine(_temp.Path, "logs");
        Directory.CreateDirectory(LogDir);
        Cli = Path.Combine(_temp.Path, "fake-agent.cmd");
        File.WriteAllText(Cli, "@echo off\r\n" + cliScript.Replace("{LOGDIR}", LogDir, StringComparison.Ordinal)); // 660-B: where move requests go
        var builder = new SchedulerConfigBuilder
        {
            Repo = Fx.Repo,
            Project = "Rig" + Guid.NewGuid().ToString("N")[..8],
            WorkspaceRoot = Path.Combine(_temp.Path, "ws"),
            LogDir = LogDir,
            WorkerCli = Cli,
            Port = FreePort(),
            DryRun = dryRun,
            // 974-B: a test that wants the file's slot count rather than -Slots (CFG-8) passes slots.
            Slots = slots,
            SlotsPinned = slotsPinned,
        };

        Config = builder.Build();
        if (format != AgentFormat.Text)
        {
            // 460-B: the fake CLI run as a json-format agent, so its raw log is rendered.
            Config = Config with { Agents = new Dictionary<string, AgentDefinition> { [AgentDefinition.DefaultName] = Scheduler.Core.Config.Agents.DefaultAgent(Config) with { Format = format } } };
        }

        if (claudeCliScript is not null)
        {
            // 959-A: a second fake CLI standing in for the claude agent, so a test can see which CLI ran.
            ClaudeCli = Path.Combine(_temp.Path, "fake-claude.cmd");
            File.WriteAllText(ClaudeCli, "@echo off\r\n" + claudeCliScript.Replace("{LOGDIR}", LogDir, StringComparison.Ordinal));
            var agents = new Dictionary<string, AgentDefinition>(Config.Agents)
            {
                ["claude"] = new AgentDefinition { Name = "claude", Cli = ClaudeCli, Args = "-p {prompt} --model {model}", Env = workerEnv ?? new Dictionary<string, string>() },
            };
            Config = Config with { Agents = agents };
        }

        Log = new ListLog();
        var settings = new Dictionary<string, object>();
        if (plannerOutbox is not null)
        {
            settings["planner"] = new { fetchRepo = Fx.Repo, outbox = plannerOutbox };
        }

        if (locks)
        {
            settings["locks"] = true;   // 1025-B: dynamic locks on (scheduler.json "locks": true)
        }

        if (toolkit is not null)
        {
            settings["toolkit"] = toolkit;
        }

        var configText = System.Text.Json.JsonSerializer.Serialize(settings);
        // 974-B: the files a test wants beside the log before the program is composed, such as the
        // workers-state.json left by the slot days.
        foreach (var (name, text) in logFiles ?? new Dictionary<string, string>())
        {
            File.WriteAllText(Path.Combine(LogDir, name), text);
        }

        Composition = HostComposition.Create(Config, configText, Clock, Log, _ => { }, TimeSpan.FromSeconds(5));
    }

    public GitFixture Fx { get; }

    public string LogDir { get; }

    public string Cli { get; }

    /// <summary>The second fake CLI, or "" when the rig has no claude agent (959-A).</summary>
    public string ClaudeCli { get; private set; } = "";

    /// <summary>What a fake CLI wrote into the log folder, or "" when it never ran.</summary>
    public string Marker(string name)
    {
        var path = Path.Combine(LogDir, name);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    public SchedulerConfig Config { get; }

    public SkewClock Clock { get; } = new();

    public ListLog Log { get; }

    public HostComposition Composition { get; }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public string[] LogFiles(string pattern) => Directory.GetFiles(LogDir, pattern);

    // A worker's log as it is now, while the worker may still be writing it (File.ReadAllText refuses a file open for writing).
    public static string ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    /// <summary>Run ticks until <paramref name="done"/> is true or the time is up.</summary>
    public bool TickUntil(Func<bool> done, int seconds = 60)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            Composition.Tick.Run();
            if (done())
            {
                return true;
            }

            Thread.Sleep(300);
        }

        return false;
    }

    /// <summary>
    /// Run ticks until <paramref name="done"/> is true, at most <paramref name="ticks"/> of them, with no
    /// wall-clock limit: between ticks it waits on the events a tick hands off instead of a clock - the git
    /// writer's queue going idle and every running worker's process ending. The two-minute waits are
    /// fallbacks that turn a hang into a failing assert; a busy PC only makes each wait longer.
    /// </summary>
    public bool TickUntilSettled(Func<bool> done, int ticks = 40)
    {
        var fallback = TimeSpan.FromMinutes(2);
        Trace("enter");
        for (var i = 0; i < ticks; i++)
        {
            Composition.Tick.Run();
            Trace($"tick {i} ran, running {Composition.Tick.Running.Count}");
            Composition.Host.Writer?.WaitIdle(fallback);
            Trace($"tick {i} writer idle");
            if (done())
            {
                Trace("done");
                return true;
            }

            foreach (var w in Composition.Tick.Running.Values.ToList())
            {
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(w.Pid);
                    Trace($"tick {i} waits for pid {w.Pid} ({p.ProcessName})");
                    p.WaitForExit(fallback);
                    Trace($"tick {i} waited for pid {w.Pid}");
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException)
                {
                    // already gone
                }
            }
        }

        return false;
    }

    // Which wait of TickUntilSettled took the time: one line per step to the file SWITCHYARD_RIG_TRACE names (nothing without it).
    private void Trace(string what)
    {
        if (Environment.GetEnvironmentVariable("SWITCHYARD_RIG_TRACE") is { Length: > 0 } file)
        {
            try
            {
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {Config.Project} {what}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // another test is writing its line: this one is left out
            }
        }
    }

    public bool RemoteHas(string path) =>
        TryGit(Fx.Bare, "cat-file", "-e", "main:" + path);

    private static bool TryGit(string dir, params string[] args)
    {
        try
        {
            GitFixture.Git(dir, args);
            return true;
        }
        catch (Xunit.Sdk.XunitException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var w in Composition.Tick.Running.Values)
        {
            try { System.Diagnostics.Process.GetProcessById(w.Pid).Kill(true); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }

        Composition.Dispose();
        Fx.Dispose();
        _temp.Dispose();
    }
}

/// <summary>The few config values a rig varies.</summary>
internal sealed class SchedulerConfigBuilder
{
    public string Repo { get; set; } = "";
    public string Project { get; set; } = "Rig";
    public string WorkspaceRoot { get; set; } = "";
    public string LogDir { get; set; } = "";
    public string WorkerCli { get; set; } = "";
    public int Port { get; set; }
    public bool DryRun { get; set; }
    public int Slots { get; set; } = 1;
    public bool SlotsPinned { get; set; } = true;
    public int MaxMinutes { get; set; } = 10;
    public int WarnMinutes { get; set; } = 2;
    public int StopGraceMinutes { get; set; } = 2;

    public SchedulerConfig Build() => new()
    {
        Repo = Repo,
        Project = Project,
        Slots = Slots,
        SlotsPinned = SlotsPinned,
        Port = Port,
        WorkspaceRoot = WorkspaceRoot,
        LogDir = LogDir,
        PollSeconds = 1,
        MaxMinutes = MaxMinutes,
        WarnMinutes = WarnMinutes,
        StopGraceMinutes = StopGraceMinutes,
        StallMinutes = 100000,
        WorkerCli = WorkerCli,
        WorkerArgs = "-p {prompt}",
        DryRun = DryRun,
    };
}
