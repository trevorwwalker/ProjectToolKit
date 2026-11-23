using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// The command line a worker process is started with (WRK-12, <c>Start-Worker</c> L1831-1836):
/// <c>cmd /c</c> so stdout and stderr both land in the log as they happen, behind a one-second
/// <c>ping</c> so the job object is in place before the agent starts its own children.
/// </summary>
public static class WorkerCommandLine
{
    private static readonly Regex QuotePattern = new(@"[\s(),;&|<>^]", RegexOptions.CultureInvariant);
    private static readonly Regex TitlePattern = new(@"[&|<>^""]", RegexOptions.CultureInvariant);

    /// <summary>
    /// The whole line, as it is started: <c>cmd /c title ... &amp; ping -n 2 127.0.0.1 &gt;nul &amp;
    /// cli args &gt; "log" 2&gt;&amp;1</c>. The title loses the characters cmd would act on.
    /// </summary>
    public static string CommandLine(string title, string cli, IEnumerable<string> args, string log) =>
        "cmd " + Arguments(title, cli, args, log);

    /// <summary>What <c>cmd.exe</c> itself is given (the part after <c>cmd</c>).</summary>
    public static string Arguments(string title, string cli, IEnumerable<string> args, string log) =>
        $"/c title {Clean(title)} & ping -n 2 127.0.0.1 >nul & {cli} {string.Join(" ", args.Select(Quote))} > \"{log}\" 2>&1";

    /// <summary>The window's title for a slot's worker (L1832).</summary>
    public static string Title(string project, int slot, string cardName) => $"{project} worker {slot} - {cardName}";

    /// <summary>An argument quoted when the shell would otherwise take it apart (L1833).</summary>
    public static string Quote(string arg) => QuotePattern.IsMatch(arg) ? $"\"{arg}\"" : arg;

    private static string Clean(string title) => TitlePattern.Replace(title, "");
}
