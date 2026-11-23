using System.Diagnostics;
using Scheduler.Core;
using Scheduler.Core.Config;
using Scheduler.Core.Launch;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Observe;
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

const string Usage ="usage: scheduler-next <repository> [-Config <file>] [-Slots n] [-Port n] [-Model m] [-NoHealthCheck] [-NoBrowser] [-Paused] [-DryRun | -Observe]\n"
    + "       scheduler-next -Pool [<pool file>] [-Slots n] [-Port n] [-NoHealthCheck] [-NoBrowser] [-Paused] [-DryRun]\n"
    + "       scheduler-next -Compare <old scheduler.log> <observe scheduler.log>";
var observeArgs = ObserveCommandLine.Parse(args);
if (observeArgs.Error.Length > 0)
{
    Console.Error.WriteLine(observeArgs.Error);
    Console.Error.WriteLine(Usage);
    return 2;
}

if (observeArgs.Compare)
{
    return CompareCommand.Run(observeArgs.CompareOld, observeArgs.CompareNew, Console.Out, Console.Error);
}

var options = CommandLine.Parse(observeArgs.Rest);

// MP-C9: -Pool runs every project of the pool file in this one process; without it, one project as always.
if (observeArgs.Pool)
{
    return Scheduler.Host.Pool.PoolProgram.Run(observeArgs.PoolFile, options, args, Console.Out, Console.Error);
}

var loaded = new ConfigLoader().Load(options);
if (!loaded.Ok || loaded.Config is not { } config)
{
    Console.Error.WriteLine(loaded.Error.Length > 0 ? loaded.Error : Usage);
    return 2;
}

ObserveSettings? observe = null;
if (observeArgs.Observe)
{
    (config, observe) = ObserveSettings.Apply(config, options.Port != 0);
}

var clock = new SystemClock();

// 690-B: besides the state log folder's scheduler.log, every line goes to a dated file in the
// repository's jobs/.logs, so the day a run stopped can be read back. An observe run writes beside
// the main scheduler's own files, not among them.
var rollingDir = observe is null
    ? FileSchedulerLog.RollingFolder(config)
    : Path.Combine(FileSchedulerLog.RollingFolder(config), ObserveSettings.Folder);
var log = new FileSchedulerLog(config.LogDir, rollingDir, clock, Console.WriteLine);
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
    observe: observe,
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
