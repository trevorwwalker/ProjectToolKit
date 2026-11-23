namespace Scheduler.Tray.Tests;

public class LogBufferTests
{
    [Fact]
    public void The_view_keeps_the_last_5000_lines()
    {
        var log = new LogBuffer();
        for (var i = 0; i < 5200; i++)
        {
            log.Add($"line {i}");
        }

        var (lines, total) = log.Snapshot();
        Assert.Equal(5000, lines.Length);
        Assert.Equal("line 200", lines[0]);
        Assert.Equal("line 5199", lines[^1]);
        Assert.Equal(5200, total);
        Assert.Equal("line 5199", log.Last);
    }

    [Fact]
    public void Since_gives_only_the_new_lines()
    {
        var log = new LogBuffer(10);
        log.Add("a");
        log.Add("b");
        var seen = log.Total;
        log.Add("c");
        log.Add("d");

        var (lines, total) = log.Since(seen);

        Assert.Equal(["c", "d"], lines!);
        Assert.Equal(4, total);
        Assert.Empty(log.Since(total).Lines!);
    }

    [Fact]
    public void Since_says_start_again_when_unseen_lines_were_dropped()
    {
        var log = new LogBuffer(3);
        log.Add("a");
        var seen = log.Total;
        for (var i = 0; i < 5; i++)
        {
            log.Add($"x{i}");
        }

        Assert.Null(log.Since(seen).Lines);
        Assert.Null(log.Since(seen + 100).Lines);
    }

    [Fact]
    public async Task Lines_from_two_threads_are_all_counted()
    {
        var log = new LogBuffer(100);
        await Task.WhenAll(
            Task.Run(() => AddMany(log, "out")),
            Task.Run(() => AddMany(log, "err")));

        Assert.Equal(2000, log.Total);
        Assert.Equal(100, log.Snapshot().Lines.Length);
    }

    private static void AddMany(LogBuffer log, string line)
    {
        for (var i = 0; i < 1000; i++)
        {
            log.Add(line);
        }
    }
}
