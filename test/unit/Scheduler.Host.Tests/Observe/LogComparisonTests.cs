using Scheduler.Host.Observe;

namespace Scheduler.Host.Tests.Observe;

/// <summary>
/// The side-by-side comparison (roadmap item 1, card 390-B): the old scheduler's scheduler.log
/// against the observe run's OBSERVE lines, one entry per observe tick that disagrees.
/// </summary>
public sealed class LogComparisonTests
{
    private static readonly LogComparison Compare = new(TimeSpan.FromSeconds(60));

    private static string Old(string time, string text) => $"[2026-11-18 {time}] {text}";

    private static string New(string time, string text) => $"{time} OBSERVE 2026-11-18 {time} {text}";

    [Fact]
    public void Matching_starts_within_the_tolerance_agree()
    {
        // DISP-21 (observe instead of start), roadmap item 1
        var report = Compare.Compare(
            [Old("10:00:00", "ProjectToolkit scheduler v2.3.16 up: 4 slots, running, agents claude"),
             Old("10:00:20", "slot 2 <- 390-B-x, pid 123, claude-opus via claude"),
             Old("10:00:50", "slot 3 <- 400-A-y (resume), pid 124, claude-opus via claude")],
            [New("10:00:15", "slot 1 <- 390-B-x"),
             New("10:00:15", "tick 1: state running, running none"),
             New("10:00:30", "tick 2: state running, running 2=390-B-x"),
             New("10:00:45", "slot 1 <- 400-A-y (resume)"),
             New("10:00:45", "tick 3: state running, running 2=390-B-x")]);

        Assert.Equal(3, report.Ticks);
        Assert.Empty(report.Disagreements);
    }

    [Fact]
    public void A_start_only_the_old_scheduler_made_is_listed_at_the_tick_that_follows_it()
    {
        // DISP-21, roadmap item 1
        var report = Compare.Compare(
            [Old("10:00:20", "slot 4 <- 390-B-x, pid 123, claude-opus via claude")],
            [New("10:00:15", "tick 1: state running, running none"),
             New("10:00:30", "tick 2: state running, running none")]);

        var d = Assert.Single(report.Disagreements);
        Assert.Equal(2, d.Tick);
        Assert.Equal(["old only: slot 4 <- 390-B-x"], d.Differences);
    }

    [Fact]
    public void A_start_only_the_new_scheduler_wanted_is_listed_at_every_tick_it_was_wanted()
    {
        // DISP-21, roadmap item 1
        var report = Compare.Compare(
            [Old("10:00:00", "ProjectToolkit scheduler v2.3.16 up: 4 slots, running, agents claude")],
            [New("10:00:15", "slot 1 <- 400-A-y (resume)"),
             New("10:00:15", "tick 1: state running, running none"),
             New("10:00:30", "slot 1 <- 400-A-y (resume)"),
             New("10:00:30", "tick 2: state running, running none")]);

        Assert.Equal([1, 2], report.Disagreements.Select(d => d.Tick));
        Assert.All(report.Disagreements, d => Assert.Equal(["new only: slot 1 <- 400-A-y (resume)"], d.Differences));
    }

    [Fact]
    public void A_start_and_a_resume_of_the_same_card_disagree()
    {
        // DISP-21, roadmap item 1
        var report = Compare.Compare(
            [Old("10:00:10", "slot 1 <- 390-B-x (resume), pid 1, m via claude")],
            [New("10:00:15", "slot 1 <- 390-B-x"),
             New("10:00:15", "tick 1: state running, running none")]);

        var d = Assert.Single(report.Disagreements);
        Assert.Equal(["old only: slot 1 <- 390-B-x (resume)", "new only: slot 1 <- 390-B-x"], d.Differences);
    }

    [Fact]
    public void The_tolerance_is_inclusive_at_its_limit()
    {
        // roadmap item 1; boundaries use >= as Watch does (test-plans/index.md)
        var atLimit = Compare.Compare(
            [Old("10:01:15", "slot 1 <- 390-B-x, pid 1, m via claude")],
            [New("10:00:15", "slot 1 <- 390-B-x"),
             New("10:00:15", "tick 1: state running, running none"),
             New("10:01:15", "tick 2: state running, running none")]);
        var beyond = Compare.Compare(
            [Old("10:01:16", "slot 1 <- 390-B-x, pid 1, m via claude")],
            [New("10:00:15", "slot 1 <- 390-B-x"),
             New("10:00:15", "tick 1: state running, running none"),
             New("10:01:16", "tick 2: state running, running none")]);

        Assert.Empty(atLimit.Disagreements);
        Assert.Equal(2, beyond.Disagreements.Count);
    }

    [Fact]
    public void Finishes_are_compared_by_card()
    {
        // TIME-13 (a worker that is gone is finished), roadmap item 1
        var report = Compare.Compare(
            [Old("10:00:10", "slot 2 finished: 390-B-x -> done after 12 min"),
             Old("10:00:40", "slot 3 finished: 400-A-y -> crashed (exit 1) after 1 min")],
            [New("10:00:15", "slot 2 finished: 390-B-x"),
             New("10:00:15", "tick 1: state running, running none"),
             New("10:00:45", "tick 2: state running, running none")]);

        var d = Assert.Single(report.Disagreements);
        Assert.Equal(2, d.Tick);
        Assert.Equal(["old only: slot 3 finished: 400-A-y"], d.Differences);
    }

    [Fact]
    public void Each_tick_in_a_different_state_is_listed()
    {
        // DISP-14 (dispatch only while running), PAGE-9 (pause, resume), roadmap item 1
        var report = Compare.Compare(
            [Old("09:59:00", "ProjectToolkit scheduler v2.3.16 up: 4 slots, paused, agents claude"),
             Old("10:00:20", "resumed from the page")],
            [New("10:00:15", "tick 1: state running, running none"),
             New("10:00:30", "tick 2: state running, running none")]);

        var d = Assert.Single(report.Disagreements);
        Assert.Equal(1, d.Tick);
        Assert.Equal(["state: old paused, new running"], d.Differences);
    }

    [Fact]
    public void The_old_states_are_read_from_its_log_texts()
    {
        // HLTH-5 (red and green), TIME-15 (quota), PAGE-9 (pause, drain), TIME-10 (stop all), roadmap item 1
        Assert.Equal("paused-red", LogComparison.OldState("health check of main abc: RED, see x.log; dispatching paused"));
        Assert.Equal("running", LogComparison.OldState("main is green again: dispatching resumed"));
        Assert.Equal("paused-quota", LogComparison.OldState("the claude account is out of quota, rate limited or signed out: dispatching paused; press Resume"));
        Assert.Equal("draining", LogComparison.OldState("draining from the page: running cards finish, nothing new starts"));
        Assert.Equal("paused", LogComparison.OldState("paused from the page"));
        Assert.Equal("draining", LogComparison.OldState("stop all from the page: every worker is asked to stop, killed after 5 min, then the scheduler exits"));
        Assert.Null(LogComparison.OldState("slot 1 <- x, pid 1"));
    }

    [Fact]
    public void Old_events_before_the_first_tick_by_more_than_the_tolerance_are_not_compared()
    {
        // roadmap item 1: only the time both ran is compared
        var report = Compare.Compare(
            [Old("09:00:00", "slot 1 <- 100-A-old, pid 1, m via claude")],
            [New("10:00:15", "tick 1: state running, running none")]);

        Assert.Empty(report.Disagreements);
    }

    [Fact]
    public void A_new_log_without_observe_ticks_says_so()
    {
        // roadmap item 1
        var report = Compare.Compare([Old("10:00:00", "x")], ["10:00:00 scheduler-next started"]);

        Assert.Equal(0, report.Ticks);
        Assert.Contains(report.Lines(), l => l.Contains("no observe ticks", StringComparison.Ordinal));
    }
}
