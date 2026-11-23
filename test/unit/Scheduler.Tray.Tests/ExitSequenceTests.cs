namespace Scheduler.Tray.Tests;

public class ExitSequenceTests
{
    [Fact]
    public async Task Exit_posts_stopall_then_waits_for_the_scheduler_to_end()
    {
        var scheduler = new FakeScheduler();

        var outcome = await scheduler.Sequence().RunAsync();

        Assert.Equal(ExitOutcome.StoppedAll, outcome);
        Assert.Equal(["post /stopall", "wait"], scheduler.Calls);
    }

    [Fact]
    public async Task A_scheduler_that_has_ended_is_left_alone()
    {
        var scheduler = new FakeScheduler { Exited = true };

        Assert.Equal(ExitOutcome.AlreadyEnded, await scheduler.Sequence().RunAsync());
        Assert.Empty(scheduler.Calls);
    }

    [Fact]
    public async Task When_the_page_refuses_the_scheduler_is_killed()
    {
        var scheduler = new FakeScheduler { PageAnswers = false };

        Assert.Equal(ExitOutcome.Killed, await scheduler.Sequence().RunAsync());
        Assert.Equal(["post /stopall", "kill", "wait"], scheduler.Calls);
    }

    [Fact]
    public async Task When_the_page_does_not_answer_the_scheduler_is_killed()
    {
        var scheduler = new FakeScheduler { PostThrows = new HttpRequestException("refused") };

        Assert.Equal(ExitOutcome.Killed, await scheduler.Sequence().RunAsync());
        Assert.Equal(["post /stopall", "kill", "wait"], scheduler.Calls);
    }

    private sealed class FakeScheduler
    {
        public bool Exited { get; set; }

        public List<string> Calls { get; } = [];

        public bool PageAnswers { get; set; } = true;

        public Exception? PostThrows { get; set; }

        public ExitSequence Sequence() => new(
            _ =>
            {
                Calls.Add("post /stopall");
                return PostThrows is { } e ? Task.FromException<bool>(e) : Task.FromResult(PageAnswers);
            },
            () => Exited,
            _ =>
            {
                Calls.Add("wait");
                Exited = true;
                return Task.CompletedTask;
            },
            () => Calls.Add("kill"));
    }
}
