namespace Scheduler.Core.Scopes;

/// <summary>
/// The glob a scope entry may use (spec B2 SC-1 and Q7 as the developer reversed them on card 961-A,
/// 2026-11-19, for AmigaX, whose cards lock <c>vm/MemoryMap.*</c> and whose categories own <c>vm/68000.*</c>):
/// <c>*</c> is any run of characters inside one path segment, <c>?</c> is one character, and <c>**</c>
/// (a run of two or more stars) also crosses <c>/</c>. A trailing <c>/</c> still means the folder and
/// every path inside it. Two entries overlap when one path can match both, which is decided exactly;
/// a character class (<c>[abc]</c>) is not matched at all, so <see cref="HasUnsupportedSyntax"/> says
/// when an entry cannot be used.
/// </summary>
public static class PathGlob
{
    /// <summary>A star that also crosses <c>/</c>, which a single <c>*</c> never does.</summary>
    private const char CrossStar = '\u0002';

    /// <summary>Whether an entry is a glob (uses <c>*</c> or <c>?</c>).</summary>
    public static bool IsGlob(string entry) => entry.Contains('*') || entry.Contains('?');

    /// <summary>Whether an entry uses something this matcher does not know (<c>[</c>, a character class).</summary>
    public static bool HasUnsupportedSyntax(string entry) => entry.Contains('[');

    /// <summary>Whether this path is one of the paths a glob entry stands for.</summary>
    public static bool Matches(string glob, string path)
    {
        var g = Tokens(Norm(glob), folder: false);
        var p = Tokens(Norm(path), folder: false);
        return g.Length == 0 || p.Length == 0
            ? string.Equals(g, p, StringComparison.Ordinal)
            : Intersect(g, p);
    }

    /// <summary>
    /// Whether one path can match both entries (SC-2, SC-3): a glob against a file (the file matches
    /// it), a glob against a folder (the folder holds a path that matches it), or two globs.
    /// </summary>
    public static bool MayOverlap(string a, string b)
    {
        var x = Norm(a);
        var y = Norm(b);
        if (x.Length == 0 || y.Length == 0)
        {
            return false;
        }

        // Two entries with no glob keep today's containment, folders and all.
        if (!IsGlob(x) && !IsGlob(y))
        {
            return Covers(x, y) || Covers(y, x);
        }

        return Intersect(Tokens(x, x.EndsWith('/')), Tokens(y, y.EndsWith('/')));
    }

    /// <summary>
    /// Whether every path the inner entry stands for is also one the outer entry stands for: what an
    /// <c>except</c> list needs before it can leave an entry out. It says no when it cannot be sure,
    /// which keeps the overlap and the two cards apart.
    /// </summary>
    public static bool Covers(string outer, string inner)
    {
        var o = Norm(outer);
        var i = Norm(inner);
        if (o.Length == 0 || i.Length == 0)
        {
            return false;
        }

        if (string.Equals(o, i, StringComparison.Ordinal))
        {
            return true;
        }

        if (!IsGlob(o))
        {
            return o.EndsWith('/') && i.StartsWith(o, StringComparison.Ordinal);
        }

        // A glob covers one named file it matches. That it covers a whole folder or another glob
        // cannot be shown here, so there it says no.
        return !IsGlob(i) && !i.EndsWith('/') && Matches(o, i);
    }

    private static string Norm(string entry) => entry.Replace('\\', '/').Trim();

    /// <summary>
    /// The entry as pattern characters: a run of stars becomes one star, crossing when the run was
    /// two or more. A folder is its own text plus <c>?**</c>: at least one character, then anything.
    /// </summary>
    private static string Tokens(string entry, bool folder)
    {
        var sb = new System.Text.StringBuilder(entry.Length + 2);
        for (var at = 0; at < entry.Length;)
        {
            if (entry[at] != '*')
            {
                sb.Append(entry[at++]);
                continue;
            }

            var run = 0;
            while (at < entry.Length && entry[at] == '*')
            {
                run++;
                at++;
            }

            sb.Append(run >= 2 ? CrossStar : '*');
        }

        if (folder)
        {
            sb.Append('?').Append(CrossStar);
        }

        return sb.ToString();
    }

    /// <summary>Whether any one path matches both patterns: the two walked together, one path character at a time.</summary>
    private static bool Intersect(string a, string b)
    {
        var seen = new bool?[a.Length + 1, b.Length + 1];
        return Step(0, 0);

        bool Step(int i, int j)
        {
            if (seen[i, j] is bool known)
            {
                return known;
            }

            seen[i, j] = false;
            var answer = i == a.Length && j == b.Length ? true
                : i == a.Length ? RestsEmpty(b, j)
                : j == b.Length ? RestsEmpty(a, i)
                : IsStar(a[i]) ? StarFromA(i, j)
                : IsStar(b[j]) ? StarFromB(i, j)
                : SameChar(a[i], b[j]) && Step(i + 1, j + 1);
            seen[i, j] = answer;
            return answer;
        }

        // a[i] is a star: it takes no path character (only a moves on), or it takes the character
        // b[j] is looking at (only b moves on). A `**/` may also stand for nothing at all, which is
        // how `src/**/a.c` holds `src/a.c`.
        bool StarFromA(int i, int j) =>
            (a[i] == CrossStar && NextIsSlash(a, i) && Step(i + 2, j))
            || Step(i + 1, j)
            || TakesChar(a[i], b[j]) && Step(i, j + 1);

        bool StarFromB(int i, int j) =>
            (b[j] == CrossStar && NextIsSlash(b, j) && Step(i, j + 2))
            || Step(i, j + 1)
            || TakesChar(b[j], a[i]) && Step(i + 1, j);
    }

    private static bool IsStar(char c) => c is '*' or CrossStar;

    private static bool NextIsSlash(string pattern, int at) => at + 1 < pattern.Length && pattern[at + 1] == '/';

    /// <summary>Whether the star can take the character the other side is looking at. An unknown character counts as yes.</summary>
    private static bool TakesChar(char star, char other) => other != '/' || star == CrossStar;

    /// <summary>Whether the two pattern characters can both look at the same path character.</summary>
    private static bool SameChar(char a, char b) =>
        a == '?' || b == '?' ? a != '/' && b != '/' : a == b;

    /// <summary>Whether the rest of the pattern can stand for no characters at all.</summary>
    private static bool RestsEmpty(string pattern, int at)
    {
        for (; at < pattern.Length; at++)
        {
            if (!IsStar(pattern[at]))
            {
                return false;
            }
        }

        return true;
    }
}
