// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 930-C (the developer, 2026-11-19): while a health check runs, the Control window's
/// <b>Check main</b> button says so - pressed, like Pause and Planner push are, reading
/// "Checking &lt;sha&gt;..." and disabled so it cannot be pressed twice - and returns to
/// "Check main" when the check ends. The live feed carries the same running state, which is what
/// lets the script change the button between page refreshes.
/// </summary>
public sealed class PageCheckMainTests
{
    // The health line the host writes while a check runs (HLTH-5): the last result, then the check
    // in progress and the sha it started on.
    private const string IdleText = "main 1a2b3c4 green at 12:20";
    private const string RunningText = "main 1a2b3c4 green at 12:20; checking 9f8e7d6c (2 min)";

    private static string Render(string healthText, bool controlsAllowed = true) =>
        new PageRenderer().Render(
            ViewsFixture.Plain() with { Health = new PageHealthView { Green = true, Text = healthText, Sha = "1a2b3c4" } },
            remote: false,
            controlsAllowed: controlsAllowed);

    // Idle: the button is its own plain self - "Check main", not pressed, pressable.
    [Fact]
    public void Page_CheckMain_IdleReadsCheckMain()
    {
        var html = Render(IdleText);

        Assert.Contains("<button class='' id='btn-health' aria-label='Check main. Build main and run its gate now.", html, StringComparison.Ordinal);
        Assert.Contains(">Check main</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Checking 9f8e7d6c", html, StringComparison.Ordinal);
    }

    // Running: the same button is pressed, disabled, and names the sha being checked (930-C).
    [Fact]
    public void Page_CheckMain_RunningIsPressedDisabledAndNamesTheSha()
    {
        var html = Render(RunningText);

        Assert.Contains("<button class='pressed' id='btn-health' disabled aria-label='Checking 9f8e7d6c...", html, StringComparison.Ordinal);
        Assert.Contains(">Checking 9f8e7d6c...</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Check main</button>", html, StringComparison.Ordinal);
        // Still the same button, still posted to /health-now.
        Assert.Contains("action='/health-now'", html, StringComparison.Ordinal);
    }

    // Remote viewer: the controls are already disabled; a running check still looks pressed and
    // still says which sha is being checked.
    [Fact]
    public void Page_CheckMain_RunningWhileRemote()
    {
        var html = Render(RunningText, controlsAllowed: false);

        Assert.Contains("<button class='pressed' id='btn-health' disabled>Checking 9f8e7d6c...</button>", html, StringComparison.Ordinal);
    }

    // The state the button and the script both read: the sha of the running check, taken from the
    // health line the host writes, and nothing at all when no check runs.
    [Theory]
    [InlineData(RunningText, "9f8e7d6c")]
    [InlineData(IdleText, "")]
    [InlineData("main not checked yet", "")]
    [InlineData("main 1a2b3c4 RED at 12:20", "")]
    public void HealthChecking_ReadsTheRunningCheckOffTheHealthLine(string text, string expected)
    {
        Assert.Equal(expected, HealthChecking.Sha(new PageHealthView { Text = text }));
    }

    // 930-C · the script drives the button from the live feed's "checking", so the press gets
    // feedback at once and the button returns to normal when the check ends, without F5.
    [Fact]
    public void Page_CheckMain_ScriptUsesTheLiveFeedsRunningState()
    {
        var script = PageScript.Html;

        Assert.Contains("btn-health", script, StringComparison.Ordinal);
        Assert.Contains("j.checking", script, StringComparison.Ordinal);
        Assert.Contains("'Checking '", script, StringComparison.Ordinal);
        Assert.Contains("classList.toggle('pressed'", script, StringComparison.Ordinal);
        Assert.Contains("disabled", script, StringComparison.Ordinal);
    }
}
