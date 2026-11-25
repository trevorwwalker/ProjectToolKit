// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Host.Program;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Pool;

/// <summary>
/// <c>scheduler-next -Pool [&lt;file&gt;]</c> (MP-C9): no repository argument; the pool file is the
/// one named or <c>%LOCALAPPDATA%\ProjectToolkit\pool.json</c> (MP-C1). Without <c>-Pool</c> nothing
/// here runs and one project starts as it always has.
/// </summary>
public static class PoolProgram
{
    /// <summary>Run the pool; the exit code (0 ended, 1 could not start, 2 bad command line or pool file).</summary>
    public static int Run(string poolFile, CommandLineOptions options, IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (options.Repo.Length > 0 || options.ConfigPath.Length > 0)
        {
            error.WriteLine("-Pool takes no repository and no -Config: the pool file names the projects.");
            return 2;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var clock = new SystemClock();
        var early = new HeldLog();
        var file = new PoolConfigFile(poolFile.Length > 0 ? poolFile : PoolConfigFile.DefaultPath(localAppData), early);
        if (!file.Read() || file.Current is not { } pool)
        {
            foreach (var line in early.Lines)
            {
                error.WriteLine(line);
            }

            error.WriteLine($"No pool to run: {file.Path} must be a JSON file naming at least one project.");
            return 2;
        }

        var logDir = PoolConfigFile.LogDirOf(pool, localAppData);
        var log = new FileSchedulerLog(logDir, Path.Combine(logDir, "logs"), clock, output.WriteLine);
        early.Target = log;
        var replaces = ReplacesSwitch.Version(args);
        if (replaces.Length > 0)
        {
            log.Write(ReplacesSwitch.ReplacedLine(Scheduler.Core.CoreInfo.Version, replaces));
        }

        log.Write($"pool file: {file.Path}");
        using var composition = PoolComposition.Create(new PoolStart
        {
            File = file,
            Options = options,
            Clock = clock,
            Log = log,
            LocalAppData = localAppData,
            OpenBrowser = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }),
            NoBrowser = NoBrowserSwitch.IsSet(args),
            ExeDir = AppContext.BaseDirectory,
            ExePath = Environment.ProcessPath,
            ShutdownEvent = ShutdownSwitch.EventName(args),
            Name = PoolNameSwitch.Name(args),
            Args = args,
        });
        try
        {
            composition.WriteStartup();
            composition.Runner.Run();
            return 0;
        }
        catch (SchedulerStartException e)
        {
            error.WriteLine(e.Message);
            return 1;
        }
    }

    // The pool file is read before the pool's log folder is known: its lines are held, then passed on.
    private sealed class HeldLog : ISchedulerLog
    {
        private ISchedulerLog? _target;

        public List<string> Lines { get; } = [];

        public ISchedulerLog? Target
        {
            get => _target;
            set
            {
                _target = value;
                foreach (var line in Lines)
                {
                    value?.Write(line);
                }

                Lines.Clear();
            }
        }

        public void Write(string line)
        {
            if (_target is { } t)
            {
                t.Write(line);
            }
            else
            {
                Lines.Add(line);
            }
        }
    }
}
