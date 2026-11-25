// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Slots;

/// <summary>Starts a card's worker on a slot: the slot's files are cleared first (WRK-6).</summary>
public sealed class SlotWorkerStarter
{
    private readonly SlotFiles _files;
    private readonly IWorkerProcesses _processes;

    /// <summary>Use <paramref name="files"/> and <paramref name="processes"/>.</summary>
    public SlotWorkerStarter(SlotFiles files, IWorkerProcesses processes)
    {
        _files = files;
        _processes = processes;
    }

    /// <summary>Clear the slot's stop, warn and message files, then start the worker (WRK-6, WRK-13).</summary>
    public StartedProcess? Start(int slot, string commandLine, string jobName, string workingDirectory,
        IReadOnlyDictionary<string, string?> environment)
    {
        _files.Clear(slot);
        return _processes.Start(commandLine, jobName, workingDirectory, environment);
    }
}
