// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

public class WorkersEnabledStateTests
{
    // WRK-3: enabled is local, persisted in workers-state.json, missing means enabled.
    [Fact]
    public void Workers_EnabledState_LocalAndPersisted()
    {
        var files = new FakeStateFiles();
        var first = new WorkersEnabledState(files);
        Assert.True(first.IsEnabled(1));
        Assert.True(first.IsEnabled(2));
        Assert.True(first.IsEnabled(3));

        first.SetEnabled(2, false);
        Assert.True(files.Files.ContainsKey(StateFile.WorkersState));

        var restarted = new WorkersEnabledState(files);
        Assert.True(restarted.IsEnabled(1));
        Assert.False(restarted.IsEnabled(2));
        Assert.True(restarted.IsEnabled(3));

        // 1.6's log-folder workers.json is read when there is no workers-state.json.
        var legacy = new FakeStateFiles();
        legacy.Write(StateFile.LegacyWorkers, """{"workers":{"1":{"enabled":true},"2":{"enabled":false}}}""");
        var old = new WorkersEnabledState(legacy);
        Assert.True(old.IsEnabled(1));
        Assert.False(old.IsEnabled(2));

        var legacyEnabled = new FakeStateFiles();
        legacyEnabled.Write(StateFile.LegacyWorkers, """{"enabled":{"3":false}}""");
        Assert.False(new WorkersEnabledState(legacyEnabled).IsEnabled(3));

        // workers-state.json wins over the legacy file; unreadable text means all enabled.
        var both = new FakeStateFiles();
        both.Write(StateFile.WorkersState, """{"enabled":{"1":true}}""");
        both.Write(StateFile.LegacyWorkers, """{"enabled":{"1":false}}""");
        Assert.True(new WorkersEnabledState(both).IsEnabled(1));
        var bad = new FakeStateFiles();
        bad.Write(StateFile.WorkersState, "not json");
        Assert.True(new WorkersEnabledState(bad).IsEnabled(1));
    }
}
