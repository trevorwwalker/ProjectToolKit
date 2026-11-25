// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// 1309-B: a card whose run has just ended is never started again from a picture of main older than that run's end
/// (DISP-8). The worker pushes its own move to done/, so only a fetch shows it; the fetch is held back while the tick
/// that reaps the worker runs.
/// </summary>
public class FinishedCardNotResumedTests
{
    // The worker waits for the test's go, moves its card to done/ and pushes it by the remote's URL, so the push
    // leaves the tracking ref alone (as when a worker's update of it is lost): the scheduler sees it only by a fetch.
    private const string Ends =
        ":wait\r\nif not exist \"{LOGDIR}\\go\" (ping -n 2 127.0.0.1 >nul & goto wait)\r\n"
        + "mkdir jobs\\done\r\n"
        + "git mv jobs/active/010-B-x.md jobs/done/010-B-x.md\r\n"
        + "git -c user.name=w -c user.email=w@w commit -qm \"jobs: done 010-B-x\"\r\n"
        + "for /f \"delims=\" %%u in ('git remote get-url origin') do git push -q \"%%u\" HEAD:main\r\n"
        + "echo LANE-RESULT: done\r\n";

    [Fact]
    public void WorkerExitsBetweenTicks_FetchHeldBack_CardIsNotStartedAgain()
    {
        using var rig = new TickRig(Ends);
        Assert.True(rig.TickUntilSettled(() => rig.Composition.Tick.Running.Count == 1));   // stops on the start tick: never watched
        var worker = rig.Composition.Tick.Running[1];

        // No fetch is in flight when the worker pushes, so none has seen its move.
        var feed = Scheduler.Host.Git.IMainFeed.Shared(rig.Fx.Repo, rig.Config.Remote, rig.Config.Branch);
        Assert.NotNull(feed);
        Assert.True(Until(() => Scheduler.Host.Git.BackgroundFetch.For(feed).Running.IsCompleted), "a fetch never ended");
        File.WriteAllText(Path.Combine(rig.LogDir, "go"), "");

        // The worker ends between two ticks: its move is on the remote, and no tick has seen it run.
        WaitForExit(worker.Pid);
        Assert.True(Until(() => rig.RemoteHas("jobs/done/010-B-x.md")), "the worker's move never reached the remote");

        // Every fetch of the rig's repository now waits for the hold file (at most 30 s), as a slow network would.
        var hold = HoldFetches(rig);
        using (new Timer(_ => File.Delete(hold), null, TimeSpan.FromSeconds(1.5), Timeout.InfiniteTimeSpan))
        {
            rig.Composition.Tick.Run();   // reaps the worker; the model is still the one from before its end
            Assert.True(Until(() => !File.Exists(hold)), "the hold was never released");
        }

        // Once a fetch since the end has completed the card is read in done/, and still nothing starts it.
        Assert.True(rig.TickUntilSettled(() => rig.Composition.Tick.Cards.Any(c => c.Name == TickRig.CardName && c.Folder == CardFolder.Done), 20));

        var log = rig.Log.Lines;
        Assert.True(
            !log.Any(l => l.Contains("started: 010-B-x (resume)", StringComparison.Ordinal)),
            "the finished card was started again:\n" + string.Join("\n", log));
        Assert.Empty(rig.Composition.Tick.Running);
    }

    // remote.origin.uploadpack runs a wrapper that waits while the hold file exists, then serves the fetch.
    private static string HoldFetches(TickRig rig)
    {
        var dir = Path.GetDirectoryName(rig.LogDir)!.Replace('\\', '/');
        var hold = Path.Combine(Path.GetDirectoryName(rig.LogDir)!, "hold-fetch");
        File.WriteAllText(hold, "");
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(rig.LogDir)!, "hold-fetch.sh"),
            $"i=0\nwhile [ -f '{dir}/hold-fetch' ] && [ $i -lt 300 ]; do sleep 0.1; i=$((i+1)); done\nexec git upload-pack \"$@\"\n");
        File.AppendAllText(Path.Combine(rig.Fx.Repo, ".git", "config"), $"[remote \"origin\"]\n\tuploadpack = sh '{dir}/hold-fetch.sh'\n");
        return hold;
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            Assert.True(p.WaitForExit(TimeSpan.FromMinutes(1)), "the worker did not end");
        }
        catch (ArgumentException)
        {
            // already gone
        }
    }

    private static bool Until(Func<bool> done)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!done())
        {
            if (DateTime.UtcNow > until)
            {
                return false;
            }

            Thread.Sleep(50);
        }

        return true;
    }
}
