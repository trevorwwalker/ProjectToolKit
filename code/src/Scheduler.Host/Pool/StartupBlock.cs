// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Reflection;
using System.Runtime.InteropServices;
using Scheduler.Core;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Pool;

/// <summary>
/// The block of <c>startup:</c> lines the log opens with (card 1012-B): tool, system, pool configuration,
/// each project and the start options, one line per item. Never a credential: a worker's <c>env</c> map is
/// listed by key name only.
/// </summary>
public static class StartupBlock
{
    /// <summary>The prefix of every line.</summary>
    public const string Prefix = "startup: ";

    /// <summary>The tool: name, version, the commit it was built from, build date, executable path.</summary>
    public static IReadOnlyList<string> Tool(string? exePath)
    {
        var info = typeof(CoreInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = info.IndexOf('+');
        var sha = plus < 0 ? "unknown" : info[(plus + 1)..].Trim();
        var built = "unknown";
        var location = typeof(CoreInfo).Assembly.Location;
        if (location.Length > 0 && File.Exists(location))
        {
            built = File.GetLastWriteTime(location).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        return
        [
            $"{Prefix}tool: Switchyard {CoreInfo.Version}, commit {sha}, built {built}",
            $"{Prefix}tool: executable {(string.IsNullOrEmpty(exePath) ? "unknown" : exePath)}",
        ];
    }

    /// <summary>The system: machine, OS, runtime, threads, memory, user, process id.</summary>
    public static IReadOnlyList<string> Machine()
    {
        var (total, free) = Memory();
        return
        [
            $"{Prefix}system: machine {Environment.MachineName}, OS {RuntimeInformation.OSDescription}",
            $"{Prefix}system: runtime {RuntimeInformation.FrameworkDescription}, {Environment.ProcessorCount} logical processors",
            $"{Prefix}system: memory {total} MB total, {free} MB free",
            $"{Prefix}system: user {Environment.UserDomainName}\\{Environment.UserName}, process id {Environment.ProcessId}",
        ];
    }

    /// <summary>The pool's configuration: file, page, remote control, workers, model servers, log folder.</summary>
    public static IReadOnlyList<string> Pool(string poolFile, PoolConfig pool, WorkerPool types, string logDir, IEnumerable<AgentDefinition> agents)
    {
        var lines = new List<string>
        {
            $"{Prefix}pool: file {poolFile}",
            $"{Prefix}pool: port {pool.Port}, listen {pool.Listen}, remote control {(pool.RemoteControl ? "on" : "off")}, maxWorkers {types.MaxWorkers}",
        };
        lines.AddRange(types.Types.Select(t =>
            $"{Prefix}pool: worker type {t.Name}: max {t.Max}, agent {Or(t.Agent)}, model {Or(t.Model)}"));
        lines.Add($"{Prefix}pool: default worker type {Or(types.DefaultType)}");
        lines.AddRange(types.Types.Select(t => t.Endpoint).Where(e => e.Length > 0).Distinct(Text.Comparer)
            .Select(e => $"{Prefix}pool: model server {e}"));
        lines.AddRange(agents.Select(a => $"{Prefix}pool: agent {a.Name}: env keys {(a.Env.Count == 0 ? "none" : string.Join(", ", a.Env.Keys))}"));
        lines.Add($"{Prefix}pool: log folder {logDir}");
        return lines;
    }

    /// <summary>
    /// One project: name, repository, weight, jobs folder, remote/branch, its workspaces (1029-B: how many,
    /// <c>slot1..slotN</c>, when <paramref name="workspaces"/> is given) and their root, health command, planner, log folder.
    /// </summary>
    public static string Project(SchedulerConfig config, int weight, bool planner, int workspaces = 0)
    {
        var health = config.HealthCommand.Length > 80 ? config.HealthCommand[..80] : config.HealthCommand;
        var folders = workspaces > 0 ? $"{workspaces} (slot1..slot{workspaces}) in {Or(config.WorkspaceRoot)}" : Or(config.WorkspaceRoot);
        return $"{Prefix}project {config.Project}: repository {config.Repo}, weight {weight}, jobs {config.JobsDir}, "
            + $"remote {config.Remote}/{config.Branch}, workspaces {folders}, health {Or(health)}, "
            + $"planner {(planner ? "on" : "off")}, log folder {config.LogDir}";
    }

    /// <summary>The start options: the command line flags.</summary>
    public static string Options(IReadOnlyList<string> args) =>
        $"{Prefix}options: {(args.Count == 0 ? "none" : string.Join(' ', args))}";

    private static string Or(string value) => value.Length == 0 ? "-" : value;

    private static (long Total, long Free) Memory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref status))
                {
                    return ((long)(status.TotalPhys / 1048576), (long)(status.AvailPhys / 1048576));
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // fall through to the runtime's figure
        }

        return (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
