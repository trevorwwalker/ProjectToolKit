// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Xunit;
using static Scheduler.Host.Tests.Pool.PoolCompositionTests;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// 1033-C (sent back): a value set on the page must survive every later step: the pool's own re-read, the Worker types
/// Save (1000-B) and a restart, in the file and on the page. Everything here is in a temp pool, never the live one.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[ParallelMethods]
public sealed class WorkspacesLifecycleTests
{
    private static int?[] OnPage(PoolComposition pool, PoolRig rig) =>
        [.. new[] { rig.A, rig.B }.Select(n => (int?)pool.PageData.PoolSnapshot().Projects.Single(p => p.Name == n).Workspaces)];

    private static int?[] InFile(PoolRig rig)
    {
        var file = new Scheduler.Host.Pool.PoolConfigFile(rig.PoolFile, new ListLog());
        Assert.True(file.Read());
        return [.. file.Current!.Projects.Select(p => p.Workspaces)];
    }

    [Fact]
    public void Card1033_SetValues_SurviveTickWorkerTypesSaveAndRestart()
    {
        using var rig = new PoolRig();
        rig.WritePoolFile(maxWorkers: 6);
        using (var pool = rig.Compose())
        {
            pool.Runner.Run();
            Assert.Equal([6, 6], OnPage(pool, rig));   // none set: the pool's maxWorkers

            Assert.True(pool.Actions.SetWorkspaces(rig.A, "8").Ok);
            Assert.True(pool.Actions.SetWorkspaces(rig.B, "4").Ok);
            Assert.Equal([8, 4], InFile(rig));
            pool.Runner.Run();
            Assert.Equal([8, 4], OnPage(pool, rig));   // the next snapshot carries them

            // The Worker types Save writes the pool file too: it keeps what it does not own.
            var types = pool.Tick.Types.Types.Select(t => new PageWorkerTypeView { Name = t.Name, Max = t.Max }).ToList();
            Assert.True(pool.Actions.SaveWorkerTypes(pool.Tick.Types.MaxWorkers, types).Ok);
            Assert.Equal([8, 4], InFile(rig));
            pool.Runner.Run();
            Assert.Equal([8, 4], OnPage(pool, rig));
        }

        using var restarted = rig.Compose();   // a restart reads the file again
        restarted.Runner.Run();
        Assert.Equal([8, 4], OnPage(restarted, rig));
    }
}
