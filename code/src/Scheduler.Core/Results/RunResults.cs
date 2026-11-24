using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Results;

/// <summary>
/// What a finished run means (A6), pure: ports <c>Alive</c> L769, <c>Result-Of</c> L2504,
/// <c>Quota-Hit</c> L2468, <c>End-Reason</c> L2492 and the reaping in <c>Tick</c> L2808-2840.
/// The host reads the files and asks the server; Core decides.
/// </summary>
public static class RunResults
{
    /// <summary>The result word of a run that handed its card back to an older lock holder (DL-12, DL-14).</summary>
    public const string Yielded = "yielded";

    private static readonly Regex Plain = new(@"LANE-RESULT:\s*([a-z-]+)", RegexOptions.IgnoreCase);
    private static readonly Regex Model = new(@"^\* .*LANE-RESULT:\s*([a-z-]+)", RegexOptions.IgnoreCase);
    private static readonly Regex JsonError = new("\"type\":\"(?:[a-z_.]*error[a-z_.]*|turn\\.failed|retry)\"|\"is_error\":true|\"subtype\":\"error|\"type\":\"result\"");

    /// <summary>
    /// Alive: not abandoned, the process exists (<paramref name="processStart"/> is its start time, or
    /// null when gone), and it started within 2 s of the recorded start (TIME-13).
    /// </summary>
    public static bool Alive(bool abandoned, DateTimeOffset? processStart, DateTimeOffset recordedStart) =>
        !abandoned && processStart is not null && Math.Abs((processStart.Value - recordedStart).TotalSeconds) <= 2;

    /// <summary>
    /// The result word of the last line that has one; null lines mean no log file. For a rendered
    /// (json-format) log only the model's own <c>* </c> lines count (TIME-14).
    /// </summary>
    public static string ResultOf(IEnumerable<string>? lines, bool rendered)
    {
        if (lines is null) return "no log";
        var rx = rendered ? Model : Plain;
        string? last = null;
        foreach (var line in lines)
        {
            var m = rx.Match(line);
            if (m.Success) last = m.Groups[1].Value;
        }
        return last ?? "no result line";
    }

    /// <summary>
    /// The CLI itself says the account is out of quota: a pattern (whole word, any case) on a
    /// non-JSON line or on a JSON error line. The model's text never counts (TIME-15).
    /// </summary>
    public static bool QuotaHit(IEnumerable<string>? rawLines, IEnumerable<string> patterns)
    {
        if (rawLines is null) return false;
        var parts = patterns.Where(p => !string.IsNullOrEmpty(p)).Select(p => "(?<![A-Za-z])(?:" + p + ")(?![A-Za-z])").ToList();
        if (parts.Count == 0) return false;
        var any = new Regex(string.Join('|', parts), RegexOptions.IgnoreCase);
        foreach (var line in rawLines)
        {
            var t = line.TrimStart();
            if (t.StartsWith('{') && !JsonError.IsMatch(t)) continue;
            if (t.StartsWith('{') && t.Contains("\"type\":\"result\"") && !t.Contains("\"is_error\":true")) continue;
            if (any.IsMatch(t)) return true;
        }
        return false;
    }

    /// <summary>Why a run ended without a result line, from what the scheduler knows (TIME-18).</summary>
    public static EndReason EndReasonOf(string stopWhy, int? exitCode)
    {
        if (Regex.IsMatch(stopWhy, @"^over the \d+ min limit", RegexOptions.IgnoreCase)) return new() { Kind = EndReasonKind.TimeLimit };
        if (Regex.IsMatch(stopWhy, "^no output for", RegexOptions.IgnoreCase)) return new() { Kind = EndReasonKind.Stalled };
        if (Regex.IsMatch(stopWhy, "^killed", RegexOptions.IgnoreCase)) return new() { Kind = EndReasonKind.Killed };
        if (stopWhy.Contains("from the page", StringComparison.OrdinalIgnoreCase)) return new() { Kind = EndReasonKind.StoppedFromPage };
        if (exitCode is not null && exitCode != 0) return new() { Kind = EndReasonKind.Crashed, ExitCode = exitCode };
        return new() { Kind = EndReasonKind.EndedEarly };
    }

    /// <summary>
    /// A dead worker's result and the retry counts after it (TIME-14 to TIME-19). <paramref name="readable"/>
    /// and <paramref name="raw"/> are the log's lines, or null when the file does not exist;
    /// <paramref name="serverUp"/> is asked only when the run has no result line and no quota text.
    /// </summary>
    public static RunOutcome Settle(
        RunningWorker w,
        IReadOnlyList<string>? readable,
        IReadOnlyList<string>? raw,
        bool rendered,
        IEnumerable<string> quotaPatterns,
        Func<bool> serverUp,
        int? exitCode,
        bool running,
        IReadOnlyDictionary<string, int> retries)
    {
        var r = new Dictionary<string, int>(retries);
        var res = ResultOf(readable, rendered);
        EndReason? reason = null;
        var pause = false;

        // Spec C8, decided by 940-A: a run count is given back, but never below 0.
        // DL-14 (dynamic locks): a yield is not a failure either: the run is given back the same way.
        if (res is "stopped" or Yielded) GiveBack(r, w.Id);

        if (res == "no result line" && readable is not null && QuotaHit(raw, quotaPatterns))
        {
            res = "out of quota";
            reason = new() { Kind = EndReasonKind.OutOfQuota };
            GiveBack(r, w.Id);
            pause = running;
        }
        if (res == "no result line" && !serverUp())
        {
            res = "model server down";
            reason = new() { Kind = EndReasonKind.ModelServerDown };
            GiveBack(r, w.Id);
        }
        // Spec C2, decided by 940-A: every run that ends without a result line counts against the
        // card - no log, crashed, ended early, over its limit or stalled - whether the card sits in
        // queue/ or active/. A run ended for a reason outside the card does not count: out of quota,
        // a down model server, or a stop the developer asked for on the page.
        if (res == "no log")
        {
            reason = new() { Kind = EndReasonKind.NoLog };
            if (!OutsideTheCard(w, reason.Kind))
            {
                Count(r, w);
            }
        }
        if (res == "no result line")
        {
            reason = EndReasonOf(w.StopWhy, exitCode);
            res = reason.ToString();
            if (!OutsideTheCard(w, reason.Kind))
            {
                Count(r, w);
            }
        }
        if (res is "done" or "review" or "waiting" or "needs-human") r.Remove(w.Id);

        return new() { Result = res, EndReason = reason, Retries = r, PauseForQuota = pause };
    }

    /// <summary>
    /// One run more against the card (DISP-11). A resume was already counted when it was dispatched,
    /// so its own run is not counted a second time (DISP-16).
    /// </summary>
    private static void Count(Dictionary<string, int> retries, RunningWorker w)
    {
        if (!w.Resume)
        {
            retries[w.Id] = (retries.TryGetValue(w.Id, out var n) ? n : 0) + 1;
        }
    }

    /// <summary>One run given back, and never below 0 (spec C8, decided by 940-A).</summary>
    private static void GiveBack(Dictionary<string, int> retries, string id)
    {
        if (retries.TryGetValue(id, out var n))
        {
            retries[id] = Math.Max(0, n - 1);
        }
    }

    /// <summary>
    /// Whether the run ended because of something outside the card: the account is out of quota, the
    /// model server is down, or the developer stopped or killed the worker from the page - including
    /// a worker that then ignored that stop and had to be killed.
    /// </summary>
    private static bool OutsideTheCard(RunningWorker w, EndReasonKind kind) =>
        kind is EndReasonKind.OutOfQuota or EndReasonKind.ModelServerDown or EndReasonKind.StoppedFromPage
        || w.StopWhy.Contains("from the page", StringComparison.OrdinalIgnoreCase);
}
