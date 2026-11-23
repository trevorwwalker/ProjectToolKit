using Scheduler.Core.Contracts;
using Scheduler.Core.Results;

namespace Scheduler.Core.Tests.Results;

/// <summary>
/// What a finished run means (TIME-13 to TIME-19; <c>Alive</c> L769, <c>Result-Of</c> L2504,
/// <c>Quota-Hit</c> L2468, <c>End-Reason</c> L2492, <c>Tick</c> L2808-2840). Test plan:
/// test-plans/time-limits.md.
/// </summary>
public class RunResultsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Patterns = SchedulerConfig.DefaultQuotaPatterns;

    private static RunningWorker Worker(bool resume = false, string stopWhy = "") => new()
    {
        Slot = 1,
        Pid = 1,
        Start = Start,
        Card = "170-A-x",
        Category = "A",
        Id = "170-A",
        Resume = resume,
        StopWhy = stopWhy,
    };

    private static RunOutcome Settle(
        string[]? readable, string[]? raw = null, bool rendered = false, bool serverUp = true,
        int? exit = null, bool running = true, RunningWorker? w = null, Dictionary<string, int>? retries = null) =>
        RunResults.Settle(w ?? Worker(), readable, raw ?? readable, rendered, Patterns, () => serverUp, exit, running,
            retries ?? new Dictionary<string, int>());

    // TIME-13
    [Fact]
    public void Time_Alive_PidAndStartTime()
    {
        Assert.True(RunResults.Alive(false, Start.AddSeconds(1.9), Start));
        Assert.True(RunResults.Alive(false, Start.AddSeconds(-1.9), Start));
        Assert.False(RunResults.Alive(false, Start.AddSeconds(2.1), Start));
        Assert.False(RunResults.Alive(false, null, Start));
        Assert.False(RunResults.Alive(true, Start, Start));
    }

    // TIME-14
    [Fact]
    public void Time_Result_LastCategoryResultLine()
    {
        Assert.Equal("done", RunResults.ResultOf(["LANE-RESULT: review", "x", "LANE-RESULT: done"], false));
        Assert.Equal("needs-human", RunResults.ResultOf(
            ["tool output: LANE-RESULT: done", "* LANE-RESULT: needs-human"], true));
        Assert.Equal("no log", RunResults.ResultOf(null, false));
        Assert.Equal("no result line", RunResults.ResultOf(["nothing here"], false));
        Assert.Equal("no result line", RunResults.ResultOf(["tool output: LANE-RESULT: done"], true));
    }

    // TIME-15: the CLI's own lines and its JSON error events count; model text never does.
    [Theory]
    [InlineData("Error: You have exceeded your monthly quota", true)]
    [InlineData("{\"type\":\"result\",\"is_error\":true,\"result\":\"rate limit\"}", true)]
    [InlineData("{\"type\":\"x\",\"subtype\":\"error_max\",\"message\":\"rate limit\"}", true)]
    [InlineData("{\"type\":\"assistant\",\"text\":\"we hit the rate limit earlier\"}", false)]
    [InlineData("ratelimited", false)]
    public void Time_Quota_DetectedFromNonModelLines(string rawLine, bool quota)
    {
        string[] readable = ["no result"];
        var o = Settle(readable, [rawLine], retries: new() { ["170-A"] = 2 });
        if (quota)
        {
            Assert.Equal("out of quota", o.Result);
            Assert.Equal(1, o.Retries["170-A"]);
            Assert.True(o.PauseForQuota);
            Assert.False(Settle(readable, [rawLine], running: false).PauseForQuota);
        }
        else
        {
            // Not the CLI's quota text, so the run ended early on the card's own account: it counts
            // against the card (spec C2, decided by 940-A).
            Assert.Equal("ended early", o.Result);
            Assert.Equal(3, o.Retries["170-A"]);
            Assert.False(o.PauseForQuota);
        }
    }

    // TIME-16
    [Fact]
    public void Time_ServerDown_NoResult()
    {
        var o = Settle(["no result"], serverUp: false, retries: new() { ["170-A"] = 2 });
        Assert.Equal("model server down", o.Result);
        Assert.Equal(1, o.Retries["170-A"]);
    }

    // TIME-17
    [Fact]
    public void Time_NoLog_NonResume_CountsRetry()
    {
        var o = Settle(null);
        Assert.Equal("no log", o.Result);
        Assert.Equal(1, o.Retries["170-A"]);

        // A resume was counted at dispatch (DISP-16): no second increment.
        var r = Settle(null, w: Worker(resume: true), retries: new() { ["170-A"] = 1 });
        Assert.Equal("no log", r.Result);
        Assert.Equal(1, r.Retries["170-A"]);
    }

    // TIME-18
    [Theory]
    [InlineData("over the 60 min limit", null, "time limit")]
    [InlineData("no output for 31 min", null, "stalled")]
    [InlineData("killed from the page", null, "killed")]
    [InlineData("stopped from the page", null, "stopped from page")]
    [InlineData("stop all from the page", null, "stopped from page")]
    [InlineData("", 3, "crashed (exit 3)")]
    [InlineData("", 0, "ended early")]
    [InlineData("", null, "ended early")]
    public void Time_EndReason_Mapping(string why, int? exit, string expected)
    {
        Assert.Equal(expected, RunResults.EndReasonOf(why, exit).ToString());
        Assert.Equal(expected, Settle(["no result"], w: Worker(stopWhy: why), exit: exit).Result);
    }

    // TIME-19
    [Fact]
    public void Time_ResultRetryBookkeeping()
    {
        var o = Settle(["LANE-RESULT: stopped"], retries: new() { ["170-A"] = 2 });
        Assert.Equal("stopped", o.Result);
        Assert.Equal(1, o.Retries["170-A"]);

        foreach (var word in new[] { "done", "review", "waiting", "needs-human" })
        {
            var d = Settle([$"LANE-RESULT: {word}"], retries: new() { ["170-A"] = 2, ["other"] = 1 });
            Assert.Equal(word, d.Result);
            Assert.False(d.Retries.ContainsKey("170-A"));
            Assert.Equal(1, d.Retries["other"]);
        }
    }

    // TIME-19, spec C8 (decided by 940-A): a run count is never below 0, whatever decrements it.
    [Fact]
    public void Time_RetryCount_ClampedAtZero()
    {
        Assert.Equal(0, Settle(["LANE-RESULT: stopped"], retries: new() { ["170-A"] = 0 }).Retries["170-A"]);
        Assert.Equal(0, Settle(["no result"], ["Error: quota exceeded"], retries: new() { ["170-A"] = 0 }).Retries["170-A"]);
        Assert.Equal(0, Settle(["no result"], serverUp: false, retries: new() { ["170-A"] = 0 }).Retries["170-A"]);

        // A count retries.json carried from before the clamp heals on the next finished run.
        Assert.Equal(0, Settle(["LANE-RESULT: stopped"], retries: new() { ["170-A"] = -3 }).Retries["170-A"]);
    }

    // TIME-17, DISP-11. Spec C2 (decided by 940-A): a card whose runs keep ending without a result
    // line is not retried without limit - each such run counts against it, in queue/ or active/.
    [Fact]
    public void Time_CardNeverClaims_EveryRunCounts()
    {
        var retries = new Dictionary<string, int>();
        for (var run = 1; run <= 5; run++)
        {
            var o = Settle(["no result"], retries: retries);
            Assert.Equal("ended early", o.Result);
            Assert.Equal(run, o.Retries["170-A"]);
            retries = new(o.Retries);
        }
    }

    // 940-A item 1: the results that say the card did not do its job.
    [Fact]
    public void Time_Retry_EveryRunWithNoResultCounts()
    {
        Assert.Equal(1, Settle(null).Retries["170-A"]);                                                        // no log
        Assert.Equal(1, Settle(["no result"], exit: 1).Retries["170-A"]);                                      // crashed (exit 1)
        Assert.Equal(1, Settle(["no result"]).Retries["170-A"]);                                               // ended early
        Assert.Equal(1, Settle(["no result"], w: Worker(stopWhy: "over the 60 min limit")).Retries["170-A"]);  // time limit
        Assert.Equal(1, Settle(["no result"], w: Worker(stopWhy: "no output for 31 min")).Retries["170-A"]);   // stalled
    }

    // 940-A item 1: and the ones that say something outside the card stopped the run.
    [Fact]
    public void Time_Retry_RunsOutsideTheCardDoNotCount()
    {
        // Out of quota and a down server give a run back rather than add one (TIME-15, TIME-16), and
        // never take a count below 0; a stop asked from the page leaves the count as it was.
        Assert.Equal(0, Settle(["no result"], ["Error: quota exceeded"], retries: new() { ["170-A"] = 0 }).Retries["170-A"]);
        Assert.Equal(0, Settle(["no result"], serverUp: false, retries: new() { ["170-A"] = 0 }).Retries["170-A"]);
        Assert.Equal(1, Settle(["no result"], ["Error: quota exceeded"], retries: new() { ["170-A"] = 2 }).Retries["170-A"]);
        Assert.Equal(1, Settle(["no result"], serverUp: false, retries: new() { ["170-A"] = 2 }).Retries["170-A"]);
        Assert.Equal(2, Settle(["no result"], exit: 1, w: Worker(stopWhy: "stopped from the page"), retries: new() { ["170-A"] = 2 }).Retries["170-A"]);
        Assert.Equal(2, Settle(["no result"], exit: 1, w: Worker(stopWhy: "killed from the page"), retries: new() { ["170-A"] = 2 }).Retries["170-A"]);
        Assert.Equal(2, Settle(["no result"], exit: 1, w: Worker(stopWhy: "stop all from the page"), retries: new() { ["170-A"] = 2 }).Retries["170-A"]);

        // A worker that ignored the stop the developer asked for on the page is still their doing.
        Assert.Equal(2, Settle(["no result"], exit: 1, w: Worker(stopWhy: "still running 2 min after a stop request (stopped from the page)"),
                               retries: new() { ["170-A"] = 2 }).Retries["170-A"]);
        Assert.Equal(2, Settle(null, w: Worker(stopWhy: "stopped from the page"), retries: new() { ["170-A"] = 2 }).Retries["170-A"]);
    }

    // TIME-17, DISP-16: a resume was counted when it was dispatched, so its own run is not counted twice.
    [Fact]
    public void Time_Retry_ResumeRunNotCountedTwice()
    {
        var o = Settle(["no result"], exit: 1, w: Worker(resume: true), retries: new() { ["170-A"] = 1 });
        Assert.Equal("crashed (exit 1)", o.Result);
        Assert.Equal(1, o.Retries["170-A"]);
    }

    // Results (DL-14, dynamic locks): a run that yields gives its run back, as a stop does.
    [Fact]
    public void Yielded_GivesTheRunBack()
    {
        var o = Settle(["LANE-RESULT: yielded"], w: Worker(resume: true), retries: new() { ["170-A"] = 2 });

        Assert.Equal(RunResults.Yielded, o.Result);
        Assert.Equal(1, o.Retries["170-A"]);
        Assert.Null(o.EndReason);
    }

    // Results (DL-14): never below 0.
    [Fact]
    public void Yielded_NeverBelowZero()
    {
        var o = Settle(["LANE-RESULT: yielded"], retries: new() { ["170-A"] = 0 });

        Assert.Equal(0, o.Retries["170-A"]);
    }

    // Results (DL-14): a yield does not count toward the stuck limit, however many times it happens.
    [Fact]
    public void Yielded_DoesNotCountTowardStuck()
    {
        var retries = new Dictionary<string, int>();
        for (var i = 0; i < 3; i++)
        {
            retries = new Dictionary<string, int>(Settle(["LANE-RESULT: yielded"], retries: retries).Retries);
        }

        Assert.False(retries.ContainsKey("170-A"));
        Assert.False(retries.GetValueOrDefault("170-A") >= new SchedulerConfig().MaxRetries);
    }
}
