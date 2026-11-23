using Scheduler.Core.Contracts;

namespace Scheduler.Core.Planner;

/// <summary>
/// The planner's copy of a card that main has changed since the planner read it (PLAN-4,
/// <c>Merge-Planner-Text</c> L1309): merged three ways like <c>git merge-file</c>, or, when the
/// planner only added to the end of its base, that addition appended to main's copy.
/// </summary>
public static class PlannerMerge
{
    /// <summary>Text with the line endings of another text: a card keeps the ones main has (<c>Like-Eol</c>).</summary>
    public static string LikeEol(string text, string like)
    {
        var lf = ToLf(text);
        return like.Contains("\r\n", StringComparison.Ordinal) ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
    }

    /// <summary>Line endings as LF (<c>To-Lf</c>).</summary>
    public static string ToLf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// The merged text with main's line endings, or null when the changes conflict.
    /// <paramref name="baseText"/> is the blob the planner read; <paramref name="ours"/> is main's
    /// copy now; <paramref name="mine"/> is the planner's.
    /// </summary>
    public static string? Merge(string baseText, string ours, string mine)
    {
        var b = ToLf(baseText);
        if (!b.EndsWith('\n'))
        {
            b += "\n";
        }

        var merged = ThreeWay(Lines(b), Lines(ToLf(ours)), Lines(ToLf(mine)));
        if (merged is not null)
        {
            return LikeEol(merged, ours);
        }

        var bb = b.TrimEnd();
        var tt = ToLf(mine).TrimEnd();
        if (tt.Length > bb.Length && tt.StartsWith(bb, StringComparison.Ordinal))
        {
            var add = tt[bb.Length..].Trim('\n');
            return LikeEol(ToLf(ours).TrimEnd() + "\n\n" + add + "\n", ours);
        }

        return null;
    }

    // Lines keep their line feed, so a last line without one differs from one with it, as in git.
    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            var end = nl < 0 ? text.Length : nl + 1;
            lines.Add(text[start..end]);
            start = end;
        }

        return lines;
    }

    // A change of base lines [Start, End) into New.
    private sealed record Hunk(int Start, int End, List<string> New);

    private static List<Hunk> Diff(List<string> a, List<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var hunks = new List<Hunk>();
        int ai = 0, bj = 0;
        while (ai < n || bj < m)
        {
            if (ai < n && bj < m && a[ai] == b[bj])
            {
                ai++;
                bj++;
                continue;
            }

            var startA = ai;
            var startB = bj;
            while ((ai < n || bj < m) && !(ai < n && bj < m && a[ai] == b[bj]))
            {
                if (bj >= m || (ai < n && lcs[ai + 1, bj] >= lcs[ai, bj + 1]))
                {
                    ai++;
                }
                else
                {
                    bj++;
                }
            }

            hunks.Add(new Hunk(startA, ai, b.GetRange(startB, bj - startB)));
        }

        return hunks;
    }

    private static string? ThreeWay(List<string> baseLines, List<string> ours, List<string> theirs)
    {
        var o = Diff(baseLines, ours);
        var t = Diff(baseLines, theirs);
        var result = new System.Text.StringBuilder();
        int pos = 0, io = 0, it = 0;
        while (io < o.Count || it < t.Count)
        {
            // The next cluster: hunks of either side that overlap or touch, grown until none does.
            var first = io < o.Count && (it >= t.Count || o[io].Start <= t[it].Start) ? o[io] : t[it];
            var s = first.Start;
            var e = first.End;
            int jo = io, jt = it;
            bool grew;
            do
            {
                grew = false;
                while (jo < o.Count && o[jo].Start <= e)
                {
                    e = Math.Max(e, o[jo].End);
                    jo++;
                    grew = true;
                }

                while (jt < t.Count && t[jt].Start <= e)
                {
                    e = Math.Max(e, t[jt].End);
                    jt++;
                    grew = true;
                }
            }
            while (grew);

            AppendBase(result, baseLines, pos, s);
            var oursText = Apply(baseLines, o, io, jo, s, e);
            var theirsText = Apply(baseLines, t, it, jt, s, e);
            if (jo == io)
            {
                result.Append(theirsText);
            }
            else if (jt == it || oursText == theirsText)
            {
                result.Append(oursText);
            }
            else
            {
                return null;
            }

            pos = e;
            io = jo;
            it = jt;
        }

        AppendBase(result, baseLines, pos, baseLines.Count);
        return result.ToString();
    }

    private static void AppendBase(System.Text.StringBuilder sb, List<string> baseLines, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            sb.Append(baseLines[i]);
        }
    }

    // The base lines [s, e) with the hunks [from, to) of one side applied.
    private static string Apply(List<string> baseLines, List<Hunk> hunks, int from, int to, int s, int e)
    {
        var sb = new System.Text.StringBuilder();
        var pos = s;
        for (var i = from; i < to; i++)
        {
            AppendBase(sb, baseLines, pos, hunks[i].Start);
            foreach (var l in hunks[i].New)
            {
                sb.Append(l);
            }

            pos = hunks[i].End;
        }

        AppendBase(sb, baseLines, pos, e);
        return sb.ToString();
    }
}
