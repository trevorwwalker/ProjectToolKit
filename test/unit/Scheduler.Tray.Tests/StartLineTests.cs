namespace Scheduler.Tray.Tests;

public class StartLineTests
{
    // The version the tray reads is the build's own (CFG-18, 951-A); it is not part of what this
    // parser looks at, so the samples below carry a made-up one.
    [Fact]
    public void The_start_line_gives_the_project_and_page()
    {
        Assert.True(StartLine.TryParse(
            "scheduler-next 1.2.3 started for ProjectToolkit: 4 slot(s), page http://localhost:8765/",
            out var project,
            out var page));

        Assert.Equal("ProjectToolkit", project);
        Assert.Equal(new Uri("http://localhost:8765/"), page);
    }

    [Fact]
    public void A_time_stamp_and_dry_run_note_are_allowed()
    {
        Assert.True(StartLine.TryParse(
            "2026-11-18 23:10:01 scheduler-next 1.2.3 started for My Game: 1 slot(s), page http://localhost:9000/ (dry run)",
            out var project,
            out var page));

        Assert.Equal("My Game", project);
        Assert.Equal(9000, page!.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tick: 2 running, 0 free")]
    [InlineData("started for X: page not-a-url")]
    public void Other_lines_are_not_the_start_line(string? line) =>
        Assert.False(StartLine.TryParse(line, out _, out _));
}
