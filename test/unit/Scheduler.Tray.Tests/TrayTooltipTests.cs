namespace Scheduler.Tray.Tests;

public class TrayTooltipTests
{
    [Theory]
    [InlineData(SchedulerState.Running, 3, "Game scheduler: running, 3 workers")]
    [InlineData(SchedulerState.Running, 1, "Game scheduler: running, 1 worker")]
    [InlineData(SchedulerState.Running, 0, "Game scheduler: running, no workers busy")]
    [InlineData(SchedulerState.Starting, null, "Game scheduler: starting")]
    [InlineData(SchedulerState.Stopping, 2, "Game scheduler: stopping, waiting for the workers")]
    [InlineData(SchedulerState.Restarting, 2, "Game scheduler: restarting; the workers keep running")]
    [InlineData(SchedulerState.Leaving, 2, "Game scheduler: ending; the workers keep running")]
    public void The_tooltip_shows_project_and_state(SchedulerState state, int? workers, string expected) =>
        Assert.Equal(expected, TrayTooltip.For("Game", state, workers, "last"));

    [Fact]
    public void The_last_line_stands_in_when_the_page_does_not_answer() =>
        Assert.Equal(
            "Game scheduler: page not answering - tick: 2 running",
            TrayTooltip.For("Game", SchedulerState.Running, null, " tick: 2 running "));

    [Fact]
    public void A_stopped_scheduler_shows_its_last_line() =>
        Assert.Equal(
            "Game scheduler: stopped - another scheduler is running",
            TrayTooltip.For("Game", SchedulerState.Exited, null, "another scheduler is running"));

    [Fact]
    public void The_tooltip_is_cut_to_what_windows_shows()
    {
        var text = TrayTooltip.For("Game", SchedulerState.Exited, null, new string('x', 300));

        Assert.Equal(TrayTooltip.MaxLength, text.Length);
        Assert.EndsWith("...", text, StringComparison.Ordinal);
    }
}
