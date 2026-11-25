// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// How the worker is started: through <c>cmd /c</c> with a one-second ping, arguments quoted
/// against the shell and the log taking both streams (WRK-12; <c>Start-Worker</c> L1831-1836).
/// Test plan: test-plans/workers.md.
/// </summary>
public class WorkerCommandLineTests
{
    private const string LogDir = @"C:\Users\dev\AppData\Local\Proj\scheduler";

    // WRK-12 (L1831-1836): cmd /c, the ping that gives the job object a second to be in place,
    // every argument that could mean something to the shell quoted, and the log for both streams.
    [Fact]
    public void Workers_CommandLine_QuotingAndPause()
    {
        var log = Path.Combine(LogDir, "010-A-20261118-112039.jsonl");
        var args = new[] { "a", "b c", "x(y)", "p;q", "m&n", "v|w", "<z>", "^" };

        var line = WorkerCommandLine.CommandLine("Proj worker 1 - 010-A-core-contracts", "copilot", args, log);

        Assert.Equal(
            "cmd /c title Proj worker 1 - 010-A-core-contracts & ping -n 2 127.0.0.1 >nul & copilot "
            + "a \"b c\" \"x(y)\" \"p;q\" \"m&n\" \"v|w\" \"<z>\" \"^\""
            + $" > \"{log}\" 2>&1",
            line);

        // The title is what the window shows, so the characters cmd would act on come out of it.
        line = WorkerCommandLine.CommandLine("Proj & worker <1> \"x\"", "copilot", args, log);
        Assert.StartsWith("cmd /c title Proj  worker 1 x & ", line, StringComparison.Ordinal);
    }
}
