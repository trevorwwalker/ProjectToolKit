using System.Text;
using System.Text.Json;

namespace Scheduler.Web.Views;

/// <summary>
/// The themes the page carries (PAGE-11): the project's own theme first, then
/// <c>theme.default.json</c> and <c>themes/*.json</c> beside the scheduler, by order and label.
/// A theme that <c>extends</c> another gets that file's CSS first. The page carries every theme
/// in its own style block and the browser picks one, so each viewer chooses for themselves.
/// Only the project theme's font and banner words are used.
/// </summary>
public sealed class PageThemes
{
    /// <summary>The key the project's own theme is carried under, as the old page named it.</summary>
    public const string ProjectKey = "project";

    /// <summary>The style every theme gets, after its own: what the page's own parts look like.</summary>
    public const string CommonCss = """
        .dep { cursor: pointer; text-decoration: underline dotted; }
        form.wthidden .wtbody { display: none; } form:not(.wthidden) .wtsum { display: none; } .wttoggle { font-weight: bold; }
        .win .mini { display: none; } .win.mini > .inner { display: none; }
        .bar .barcard { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; max-width: 45%; }
        .win > .bar { position: relative; }
        .bar .prog { position: absolute; left: 0; bottom: 0; height: 3px; background: currentColor; opacity: .55; pointer-events: none; transition: width 1s linear; }
        .bar .prog.late { background: #e8a000; opacity: .9; }
        .win.mini .mini { display: inline-flex; align-items: center; gap: 6px; white-space: nowrap; }
        .spin { display: inline-block; width: 0.8em; height: 0.8em; border: 2px solid currentColor; border-right-color: transparent; border-radius: 50%; animation: spin 1s linear infinite; }
        .mini.idle .spin { animation: none; opacity: 0.4; }
        @keyframes spin { to { transform: rotate(360deg); } }
        .dep.on { font-weight: bold; }
        button.pressed { background: transparent; border: 1px solid currentColor; box-shadow: inset 0 2px 6px rgba(0, 0, 0, .45); transform: translateY(1px); font-weight: bold; }
        button.pressed.ok { color: var(--ok); } button.pressed.warn { color: var(--warn); }
        tr.hl > td { background: rgba(255, 136, 0, .3); }
        .win.hl { outline: 3px solid #ff8800; outline-offset: 1px; }
        .win.hl > .bar { background: #ff8800; color: #000022; }
        html, body { overflow-anchor: none; }
        .bar .g { margin: 0; }
        .notice { background: #ff8800; color: #000022; padding: 8px 12px; margin: 0 0 12px; border: 2px solid #000022; }
        .bar a.barlink { color: inherit; text-decoration: none; } .bar a.barlink:hover { text-decoration: underline; }
        .bar .g:hover::after { bottom: auto; top: calc(100% + 6px); }
        pre.log { max-height: 22em; max-height: 16lh; overflow: auto; }
        /* 965-C: a worker's log box is only ever appended to, but a run that has written nothing, or
           has only just started, still holds the height of a few lines. An empty box used to be one
           line high and the windows under it jumped as the log filled. */
        pre.log { min-height: 3em; }
        .bar b .cardlink { margin-left: 8px; white-space: nowrap; }
        /* 630-A (roadmap item 3): the Trends chart under Recent events. The bars and the line carry
           the theme's own colours as attributes; the legend below them is these few rules.
           963-C (the developer, 2026-11-19: "the graph should more comfortably fit the space"): the
           svg is as wide as the window and as tall as its 3:1 viewBox says, so the fourteen day slots
           spread over the whole window; the min/max keep it usable in a narrow column and on a huge
           screen. The labels scale with it, so alternate ones drop out when the window is narrow. */
        .trend svg { display: block; width: 100%; height: auto; min-height: 90px; max-height: 380px; }
        .trend .tday, .trend .tmax, .trend .tzero { fill: var(--muted); font-size: 12px; font-family: inherit; }
        @media (max-width: 760px) { .trend .tday.alt { display: none; } }
        .trendlegend { display: flex; flex-wrap: wrap; gap: 3px 10px; align-items: center; font-size: 11px; color: var(--muted); margin-top: 2px; }
        .trendlegend .tswatch { display: inline-flex; align-items: center; padding-left: 13px; position: relative; }
        .trendlegend .tswatch::before { content: ''; position: absolute; left: 0; top: 3px; width: 9px; height: 9px; background: currentColor; }
        .trendproj + .trendproj { margin-top: 10px; border-top: 1px solid var(--line); padding-top: 6px; }
        .trendname { font-size: 11px; color: var(--muted); }
        /* 958-C (the developer, 2026-11-19: "the right got dramatically wider, to the loss of the
           left"): a 1fr track grows to the min-content width of what sits in it, so one unbreakable
           card name, queue note, worker-type name or event line in the side column held the whole
           right column open and took that width from the Control and worker windows. Any text on the
           desk may now break anywhere, which makes the widest thing in a cell a word rather than a
           whole name or path. It wraps and stays on show - nothing is clipped away into a tooltip. */
        .desk { overflow-wrap: anywhere; }
        /* 970-C: the category chip is one line, whatever the theme. */
        .chip { display: inline-block; white-space: nowrap; overflow-wrap: normal; padding: 0 6px; border-radius: 9px; font-size: 11px; line-height: 16px; cursor: default; }
        /* 968-C (the developer, 2026-11-19, screenshot of the Queue): 958-C's "break anywhere" reached
           every cell, so the Queue squashed its short columns - "LOG" and "LANE" one letter per line,
           "log" as "lo/g", "957/-B", and a card name broken at every hyphen while the State column sat
           with room to spare. In a list table a column is now one of three kinds. Short columns (the
           number, Log, Category, Depends on, When, Worker, Result, Time and the buttons) keep their field
           on one line and take only the width their content needs. The card column keeps the name on a
           line too, and shortens it with an ellipsis rather than breaking it, with the whole name in
           the tooltip; 22ch is the floor it never goes below, so the id stays readable. The state or
           note column is the only one that wraps, and it breaks inside a word only when the word alone
           is too wide for the column - a log path, an unbroken card name. The rules are outside the
           media query, so they hold at any zoom, and are named by the window's own id, which outranks
           958-C's "#col-side td { min-width: 0 }" while leaving it in place: nothing in the side
           column can hold it open beyond a card's 22ch. */
        /* 968-C, the developer's second look (2026-11-19 20:50, 1920x1080 at 100%): the widths came
           out backwards - the card column wide and half empty, State squeezed to ~120 px with a note
           a word a line. Two causes: every header said "width: 1%", so State was sized like a short
           column and the spare width went elsewhere; and a table cell's own max-width is ignored by
           the automatic table layout, so the card column took the whole name's width. Now only the
           short and card columns are sized to content (width: 1%), the card name's 26ch cap sits on
           its link, which the table does respect when it measures the column, and the note column has
           no width, so it takes everything left over and wraps by words. The waiting window follows the
           same three kinds; its question rows put the choices under the question. Buttons and status
           words never break inside a word, whatever 958-C's "break anywhere" says around them. */
        #w-queue table, #w-events table, #w-waiting table { table-layout: auto; width: 100%; }
        #w-queue th, #w-events th, #w-waiting th { white-space: nowrap; overflow-wrap: normal; }
        #w-queue td.nw, #w-events td.nw, #w-waiting td.nw { white-space: nowrap; overflow-wrap: normal; width: 1%; }
        #w-queue td.card, #w-events td.card, #w-waiting td.card { white-space: nowrap; overflow-wrap: normal; width: 1%; min-width: 22ch; overflow: hidden; text-overflow: ellipsis; }
        #w-queue td.card a, #w-events td.card a, #w-waiting td.card a { display: block; max-width: 26ch; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        #w-queue td.wrap, #w-events td.wrap, #w-waiting td.wrap { white-space: normal; overflow-wrap: anywhere; }
        /* Recent events has no note column on a run's row, so there the card takes the spare width:
           its link adds nothing to the column's measure (width 0) and then fills the cell, and an
           event's line of text across the row adds nothing either, so it cannot widen the short
           columns it spans. */
        #w-events td.card { width: 100%; }
        #w-events td.card a { width: 0; min-width: 100%; max-width: none; }
        #w-events td.wrap[colspan] > span { display: block; width: 0; min-width: 100%; }
        .desk th { overflow-wrap: normal; }
        .desk button, .desk td.nw span, .desk .tag { white-space: nowrap; overflow-wrap: normal; }
        .qchoices { display: flex; flex-wrap: wrap; gap: 4px 6px; margin: 4px 0 2px; }
        .qchoices .g, .qchoices form { margin: 0; }
        /* 590-C (the developer, 2026-11-18): one two-column layout from 1400 px up, the main column
           on the left and Queue above Recent events on the right. 958-C (the developer, 2026-11-19:
           "the right got dramatically wider, to the loss of the left") made the split proportional:
           a 1600 px cap is a fixed width, and at 60% zoom the page is 2542 CSS px wide, so the left
           stopped growing at 1600 px and the right took the remaining 900 px of the screen. The two
           tracks now share the width in the 60/40 proportions the developer settled on live (972-C), so
           the page looks the same whatever the zoom; the right never drops below the 440 px that is
           still readable, and below 1400 px the page still stacks as one column. */
        @media (min-width: 1400px) {
          .desk { max-width: none; margin: 0; display: grid; grid-template-columns: minmax(0, 60fr) minmax(440px, 40fr); grid-template-areas: "top top" "main side"; column-gap: 16px; row-gap: 0; align-items: start; }
          #col-top { grid-area: top; min-width: 0; }
          #col-main { grid-area: main; min-width: 0; display: flex; flex-direction: column; }
          #col-side { grid-area: side; min-width: 0; position: sticky; top: 60px; max-height: calc(100vh - 76px); overflow-y: auto; }
          /* 958-C: nor may any box inside the side column hold its track open - each of them is free
             to shrink to nothing, so 590-C's widths hold whatever a card name or note happens to be. */
          #col-side > .col, #col-side .win, #col-side .inner, #col-side table, #col-side td, #col-side th { min-width: 0; max-width: 100%; }
        }
        """;

    /// <summary>The style used when no theme file could be read at all.</summary>
    internal const string FallbackCss = """
        body { margin: 0; font: 14px/1.4 system-ui, sans-serif; background: #fff; color: #111; }
        .screen { display: flex; gap: 12px; padding: 6px 12px; border-bottom: 1px solid #ccc; }
        .screen .t { flex: 1; }
        .desk { padding: 12px; }
        .win { border: 1px solid #ccc; margin: 0 0 12px; }
        .bar { display: flex; gap: 8px; padding: 4px 8px; background: #eee; }
        .bar b { flex: 1; }
        .inner { padding: 8px; }
        .slots { display: grid; grid-template-columns: minmax(0, 1fr); gap: 12px; }
        table { border-collapse: collapse; width: 100%; } td, th { text-align: left; padding: 2px 6px; }
        .g { display: inline-block; margin: 0 6px 4px 0; }
        pre.log { overflow: visible; white-space: pre-wrap; }
        form.send { display: flex; gap: 6px; margin-top: 6px; } form.send input { flex: 1; font: inherit; }
        .health-red { background: #c00; color: #fff; padding: 8px; margin: 0 0 12px; }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private PageThemes(
        IReadOnlyList<PageThemeEntry> entries,
        string redHealthText,
        string redHealthDetail,
        string fontFile,
        bool fontExists)
    {
        Entries = entries;
        RedHealthText = redHealthText;
        RedHealthDetail = redHealthDetail;
        FontFile = fontFile;
        FontExists = fontExists;
    }

    /// <summary>The themes in selector order, project theme first.</summary>
    public IReadOnlyList<PageThemeEntry> Entries { get; }

    /// <summary>The project theme's red-health words, with <c>{sha}</c> still in the detail line.</summary>
    public string RedHealthText { get; }

    /// <summary>The project theme's red-health detail line; <c>{sha}</c> is the red commit.</summary>
    public string RedHealthDetail { get; }

    /// <summary>The project theme's font file, or empty. Only the project theme's font is used.</summary>
    public string FontFile { get; }

    /// <summary>Whether that font file is there; a missing font is no font.</summary>
    public bool FontExists { get; }

    /// <summary>The scheduler's own theme files: the words shown when a theme has none.</summary>
    public const string DefaultRedHealth = "main does not build or pass its gate. Dispatching is held.";

    /// <summary>The scheduler's own theme files: the detail line shown when a theme has none.</summary>
    public const string DefaultRedHealthDetail = "main {sha} is red.";

    /// <summary>A page with no theme files at all: one built-in style, the old page's last resort.</summary>
    public static PageThemes BuiltIn { get; } = new(
        [new PageThemeEntry(ProjectKey, "Built-in", FallbackCss)],
        DefaultRedHealth,
        DefaultRedHealthDetail,
        "",
        fontExists: false);

    /// <summary>
    /// Reads the project theme (or <c>theme.default.json</c> when none is named) and the built-ins
    /// in <paramref name="schedulerFolder"/>. Unreadable files fall back as the old page did.
    /// </summary>
    /// <param name="schedulerFolder">Where <c>theme.default.json</c> and <c>themes/</c> live.</param>
    /// <param name="projectThemeFile">The file named by <c>page.theme</c>, or null.</param>
    /// <param name="repoRoot">The repository root a repo-relative font path is tried from; null walks up from the theme's folder.</param>
    /// <param name="log">Told once, in one line, when the theme names a font that is nowhere to be found.</param>
    public static PageThemes Load(string schedulerFolder, string? projectThemeFile = null, string? repoRoot = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(schedulerFolder);

        var defaultFile = Path.Combine(schedulerFolder, "theme.default.json");
        var ownFile = string.IsNullOrWhiteSpace(projectThemeFile) ? null : projectThemeFile;
        if (ownFile is not null && !File.Exists(ownFile))
        {
            ownFile = null;
        }

        ownFile ??= defaultFile;
        var own = Read(ownFile);

        var ownLabel = "Built-in";
        var ownCss = FallbackCss;
        if (own is not null)
        {
            if (!string.IsNullOrEmpty(own.Label))
            {
                ownLabel = own.Label;
            }

            if (!string.IsNullOrEmpty(own.Css))
            {
                ownCss = own.Css;
            }
        }

        var entries = new List<PageThemeEntry> { new PageThemeEntry(ProjectKey, ownLabel, ownCss) };

        var builtIn = new List<string> { defaultFile };
        var themesFolder = Path.Combine(schedulerFolder, "themes");
        if (Directory.Exists(themesFolder))
        {
            builtIn.AddRange(Directory.GetFiles(themesFolder, "*.json"));
        }

        var extras = new List<(string Key, ThemeRead? Theme)>();
        foreach (var file in builtIn)
        {
            if (SameFile(file, ownFile))
            {
                continue;   // already first in the selector
            }

            var theme = Read(file);
            if (theme is not null)
            {
                extras.Add((ThemeKey(file), theme));
            }
        }

        foreach (var (key, theme) in extras.OrderBy(e => e.Theme!.Order).ThenBy(e => e.Theme!.Label, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(new PageThemeEntry(key, theme!.Label, theme.Css));
        }

        var font = own?.Font ?? "";
        var fontExists = font.Length > 0 && File.Exists(ResolveFont(ownFile, font, repoRoot));
        if (font.Length > 0 && !fontExists)
        {
            log?.Invoke($"themes: font {font} named by {Path.GetFileName(ownFile)} was not found beside the theme or from the repository root; the page uses a system font");
        }

        return new PageThemes(
            entries,
            own?.RedHealth is { Length: > 0 } red ? red : DefaultRedHealth,
            own?.RedHealthDetail is { Length: > 0 } detail ? detail : DefaultRedHealthDetail,
            font,
            fontExists);
    }

    /// <summary>
    /// The font's full path: beside the theme that names it first, then from the repository root
    /// (<paramref name="repoRoot"/>, or each folder above the theme). The beside-the-theme path
    /// when neither exists.
    /// </summary>
    public static string ResolveFont(string? themeFile, string font, string? repoRoot = null)
    {
        if (Path.IsPathRooted(font) || themeFile is null)
        {
            return font;
        }

        var themeDir = Path.GetDirectoryName(Path.GetFullPath(themeFile)) ?? "";
        var beside = Path.Combine(themeDir, font);
        if (File.Exists(beside))
        {
            return beside;
        }

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var fromRoot = Path.Combine(repoRoot, font);
            return File.Exists(fromRoot) ? fromRoot : beside;
        }

        for (var dir = Path.GetDirectoryName(themeDir); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            var above = Path.Combine(dir, font);
            if (File.Exists(above))
            {
                return above;
            }
        }

        return beside;
    }

    /// <summary>The theme's head: the font, every theme in its own style block, then the script that keeps the choice.</summary>
    public string Head()
    {
        var sb = new StringBuilder();
        if (FontExists)
        {
            sb.Append("<style>@font-face { font-family: 'ThemeFont'; src: url('/font'); }</style>");
        }

        var first = true;
        foreach (var entry in Entries)
        {
            sb.Append("<style data-theme='").Append(entry.Key).Append("' media='")
              .Append(first ? "all" : "not all").Append("'>\n").Append(entry.Css).Append("\n</style>");
            first = false;
        }

        sb.Append("<style>\n").Append(CommonCss).Append("\n</style>");
        sb.Append(SelectorScript);
        return sb.ToString();
    }

    /// <summary>The appearance selector: every theme by the order <see cref="Entries"/> holds.</summary>
    public string Selector()
    {
        var sb = new StringBuilder("<select id='themesel' title='Appearance (kept in this browser)' aria-label='Appearance' style='font: inherit'>");
        foreach (var entry in Entries)
        {
            sb.Append("<option value='").Append(entry.Key).Append("'>").Append(Encode(entry.Label)).Append("</option>");
        }

        return sb.Append("</select>").ToString();
    }

    /// <summary>The script that applies the theme this browser chose, before the body is drawn.</summary>
    internal const string SelectorScript = """
        <script>
        function setTheme(k) {
          var all = document.querySelectorAll('style[data-theme]'), ok = false;
          for (var i = 0; i < all.length; i++) if (all[i].getAttribute('data-theme') === k) ok = true;
          if (!ok) return false;
          for (var j = 0; j < all.length; j++) all[j].media = all[j].getAttribute('data-theme') === k ? 'all' : 'not all';
          return true;
        }
        var themeMem = null;
        function storedTheme() { try { return localStorage.getItem('scheduler-theme'); } catch (x) { return themeMem; } }
        function storeTheme(k) { themeMem = k; try { localStorage.setItem('scheduler-theme', k); } catch (x) {} }
        (function () { var k = storedTheme(); if (k) setTheme(k); })();
        // The appearance selector: a choice applies at once and is kept in this browser; once the
        // body is drawn, the selector shows the stored choice. Here rather than in the page's
        // script, so it works on every page that has the selector, whatever that script does.
        function syncThemeSel() {
          var sel = document.getElementById('themesel'), k = storedTheme();
          if (sel && k && setTheme(k)) sel.value = k;
        }
        document.addEventListener('change', function (e) {
          if (!e.target || e.target.id !== 'themesel') return;
          if (setTheme(e.target.value)) storeTheme(e.target.value);
        });
        document.addEventListener('DOMContentLoaded', syncThemeSel);
        </script>
        """;

    private static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static bool SameFile(string a, string? b)
    {
        if (string.IsNullOrEmpty(b))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ThemeKey(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (name.StartsWith("theme.", StringComparison.OrdinalIgnoreCase))
        {
            name = name["theme.".Length..];
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsAsciiLetter(c) || char.IsAsciiDigit(c) || c == '-' || c == '_')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Reads one theme file, following <c>extends</c>. Null when it cannot be read.</summary>
    private static ThemeRead? Read(string file, int depth = 0)
    {
        if (depth > 16 || !File.Exists(file))
        {
            return null;
        }

        ThemeFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ThemeFileDto>(File.ReadAllText(file, Encoding.UTF8), JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }

        if (dto is null)
        {
            return null;
        }

        var css = string.Join('\n', (dto.Css ?? []).Select(line => line ?? ""));
        if (!string.IsNullOrWhiteSpace(dto.Extends))
        {
            var baseTheme = Read(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file) ?? "", dto.Extends!)), depth + 1);
            if (baseTheme is not null)
            {
                css = baseTheme.Css + "\n" + css;
            }
        }

        var name = string.IsNullOrWhiteSpace(dto.Name) ? Path.GetFileNameWithoutExtension(file) : dto.Name!;
        return new ThemeRead(
            string.IsNullOrWhiteSpace(dto.Label) ? name : dto.Label!,
            css,
            dto.Order ?? 50,
            dto.Font?.File ?? "",
            dto.Text?.RedHealth,
            dto.Text?.RedHealthDetail);
    }

    private sealed record ThemeRead(string Label, string Css, int Order, string Font, string? RedHealth, string? RedHealthDetail);

    private sealed record ThemeFileDto
    {
        public string? Name { get; init; }

        public string? Label { get; init; }

        public int? Order { get; init; }

        public string? Extends { get; init; }

        public string?[]? Css { get; init; }

        public ThemeFontDto? Font { get; init; }

        public ThemeTextDto? Text { get; init; }
    }

    private sealed record ThemeFontDto
    {
        public string? File { get; init; }
    }

    private sealed record ThemeTextDto
    {
        public string? RedHealth { get; init; }

        public string? RedHealthDetail { get; init; }
    }
}
