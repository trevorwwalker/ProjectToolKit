// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core;
using Scheduler.Core.Config;
using Scheduler.Core.Launch;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Program;
using Scheduler.Host.Tick;

// 670-B: the console lines are UTF-8 whatever the console code page is, so the tray's log window shows
// what a UTF-8 console shows. Before anything writes a line.
ConsoleOutput.TrySetUtf8();

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine(CoreInfo.Version);
    return 0;
}

// 1302-B: --help, -h, -? or /? prints the usage and exits, before anything else is read or written.
if (LaunchArguments.IsHelp(args))
{
    Console.WriteLine(LaunchArguments.HelpText(CoreInfo.Version));
    return 0;
}

// WRK-14, job card 570-B: the agent hook that hands a worker the developer's message. Before
// anything else, so it returns in well under the hook's timeout.
if (args.Length >= 1 && args[0] == MessageHook.Switch)
{
    return MessageDelivery.Run(
        args.Length > 1 ? args[1] : "",
        Environment.GetEnvironmentVariable(MessageHook.FileVariable),
        () => Console.IsInputRedirected ? Console.In.ReadToEnd() : "",
        Console.Out,
        DateTime.UtcNow);
}

// Dynamic locks (dynamic-locks.md section 3, 1020-B): a worker asks for a file. Files only, never a scheduler.
if (args.Length >= 1 && args[0] == LockCommand.Switch)
{
    return LockCommand.Run(args[1..], Environment.GetEnvironmentVariable, Console.Out);
}

// Dynamic locks (DL-18, 1021-B): the pre-push hook asks whether a push to main touches only held files.
if (args.Length >= 1 && args[0] == CheckPushCommand.Switch)
{
    return CheckPushCommand.Run(args[1..], Environment.GetEnvironmentVariable, Console.In, Console.Out, Environment.CurrentDirectory);
}

const string Usage ="usage: scheduler-next <repository> [-Config <file>] [-Slots n] [-Port n] [-Model m] [-NoHealthCheck] [-NoBrowser] [-Paused] [-DryRun]\n"
    + "       scheduler-next -Pool [<pool file>] [-Slots n] [-Port n] [-NoHealthCheck] [-NoBrowser] [-Paused] [-DryRun]";
var launch = LaunchArguments.Parse(args);
var options = CommandLine.Parse(launch.Rest);

// MP-C9: -Pool runs every project of the pool file in this one process; without it, one project as always.
if (launch.Pool)
{
    return Scheduler.Host.Pool.PoolProgram.Run(launch.PoolFile, options, args, Console.Out, Console.Error);
}

var loaded = new ConfigLoader().Load(options);
if (!loaded.Ok || loaded.Config is not { } config)
{
    Console.Error.WriteLine(loaded.Error.Length > 0 ? loaded.Error : Usage);
    return 2;
}

var clock = new SystemClock();

// 690-B: besides the state log folder's scheduler.log, every line goes to a dated file in the
// repository's jobs/.logs, so the day a run stopped can be read back.
var log = new FileSchedulerLog(config.LogDir, FileSchedulerLog.RollingFolder(config), clock, Console.WriteLine);
foreach (var line in loaded.LogLines)
{
    log.Write(line);
}

// 1081-B: the one git.exe: scheduler.json's setting (the pool file belongs to -Pool), named in the log's start block.
GitExecutable.Configure(GitExecutable.Choose(null, "", [(config.Project, GitExecutable.SettingOf(loaded.ConfigText))]));
foreach (var line in GitExecutable.DescribeLines())
{
    log.Write(line);
}

// 967-B: when a deploy swapped this build into out\windows\scheduler, the log says which version it took
// over from, so the two runs can be told apart afterwards. The tray passes the switch; a plain start has it
// absent and logs nothing extra.
var replaces = ReplacesSwitch.Version(args);
if (replaces.Length > 0)
{
    log.Write(ReplacesSwitch.ReplacedLine(CoreInfo.Version, replaces));
}

// WRK-14, job card 570-B: the settings every Claude worker is given with --settings.
MessageDelivery.WriteClaudeSettings(config.LogDir, Environment.ProcessPath ?? "", log);

using var composition = HostComposition.Create(
    config,
    loaded.ConfigText,
    clock,
    log,
    url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }),
    exeDir: AppContext.BaseDirectory,
    shutdownEvent: ShutdownSwitch.EventName(args));
try
{
    composition.Runner.Run();
    return 0;
}
catch (SchedulerStartException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
