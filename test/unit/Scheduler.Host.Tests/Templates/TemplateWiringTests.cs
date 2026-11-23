using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Tick;

namespace Scheduler.Host.Tests.Templates;

/// <summary>The template refresh as the running scheduler has it (card 540-B): set up from the config and run by the tick.</summary>
[ParallelMethods]
public sealed class TemplateWiringTests
{
    [Fact]
    public void The_tick_refreshes_the_generic_files_from_the_toolkit_in_the_config()
    {
        using var toolkit = new GitFixture();
        toolkit.Push("templates", new Dictionary<string, string?> { ["assets/templates/jobs/README.md"] = "rules v1\n" });
        GitFixture.Git(toolkit.Repo, "pull", "-q", "origin", "main");
        using var rig = new TickRig("echo hi\r\n", toolkit: toolkit.Repo);

        Assert.NotNull(rig.Composition.Host.Templates);
        Assert.True(rig.TickUntilSettled(() => GitFixture.Git(rig.Fx.Bare, "ls-tree", "-r", "--name-only", "main").Contains("jobs/README.md", StringComparison.Ordinal)));
        Assert.Equal("rules v1", GitFixture.Git(rig.Fx.Bare, "show", "main:jobs/README.md"));
    }

    [Fact]
    public void A_paused_project_runs_no_template_refresh_and_resuming_runs_it_at_the_next_tick()
    {
        using var toolkit = new GitFixture();
        toolkit.Push("templates", new Dictionary<string, string?> { ["assets/templates/jobs/README.md"] = "rules v1\n" });
        GitFixture.Git(toolkit.Repo, "pull", "-q", "origin", "main");
        using var rig = new TickRig("echo hi\r\n", toolkit: toolkit.Repo);
        var tick = rig.Composition.Tick!;
        tick.State = Scheduler.Core.Contracts.SchedulerState.Paused;

        Assert.False(rig.TickUntilSettled(() => GitFixture.Git(rig.Fx.Bare, "ls-tree", "-r", "--name-only", "main").Contains("jobs/README.md", StringComparison.Ordinal), ticks: 8));
        Assert.Equal(0, rig.Composition.Host.Templates!.Checks);

        tick.State = Scheduler.Core.Contracts.SchedulerState.Running;

        Assert.True(rig.TickUntilSettled(() => GitFixture.Git(rig.Fx.Bare, "ls-tree", "-r", "--name-only", "main").Contains("jobs/README.md", StringComparison.Ordinal)));
    }

    [Fact]
    public void No_refresh_without_a_toolkit()
    {
        using var rig = new TickRig("echo hi\r\n");

        Assert.Null(rig.Composition.Host.Templates);
    }

    [Fact]
    public void A_pool_names_its_toolkit_at_the_top()
    {
        Assert.Equal("C:/ProjectToolkit", PoolConfig.Parse("{\"toolkit\": \"C:/ProjectToolkit\", \"projects\": []}").Toolkit);
        Assert.Equal("", PoolConfig.Parse("{}").Toolkit);
    }
}
