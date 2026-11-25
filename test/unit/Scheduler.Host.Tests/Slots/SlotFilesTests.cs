// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

public class SlotFilesTests
{
    private sealed class Recorder(SlotFiles files, int slot) : IWorkerProcesses
    {
        public List<bool> FilesExistAtStart { get; } = new();

        public StartedProcess? Start(string commandLine, string jobName, string workingDirectory, IReadOnlyDictionary<string, string?> environment)
        {
            FilesExistAtStart.Add(files.Paths(slot).Any(File.Exists));
            return new StartedProcess { Pid = 1, Start = DateTimeOffset.Now };
        }

        public bool IsAlive(int pid, DateTimeOffset start) => false;
        public DateTimeOffset? CmdStartTime(int pid) => null;
        public void EndJob(string jobName, int pid) { }
        public int? ExitCode(int pid) => null;
        public long? LogSize(string path) => null;
        public DateTimeOffset? LogLastWrite(string path) => null;
    }

    // WRK-6: before a start the slot's stop, warn and message files are deleted.
    [Fact]
    public void Workers_ClearSlotFiles_BeforeStart()
    {
        using var dir = new TempDir();
        var files = new SlotFiles(dir.Path);
        foreach (var name in new[] { "stop-slot1", "warn-slot1", "msg-slot1.txt", "msg-slot1.read.txt", "stop-slot2" })
        {
            File.WriteAllText(Path.Combine(dir.Path, name), "x");
        }
        var processes = new Recorder(files, 1);

        var started = new SlotWorkerStarter(files, processes).Start(1, "cmd", "job", dir.Path, new Dictionary<string, string?>());

        Assert.NotNull(started);
        Assert.Equal([false], processes.FilesExistAtStart);
        Assert.False(File.Exists(Path.Combine(dir.Path, "stop-slot1")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "msg-slot1.txt")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "stop-slot2")), "another slot's files stay");
    }
}
