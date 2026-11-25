// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Accounting;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The Trends chart (roadmap item 3, card 630-A): the last 14 local days along the bottom, the
/// cards each worker type finished as a stacked bar, and the runs as a thin line, in plain inline
/// SVG - no script library, and every colour a theme variable so the chart follows the theme the
/// reader chose.
/// 963-C (the developer, 2026-11-19: "the graph should more comfortably fit the space"): the plot is
/// drawn in a 3:1 box whose width is the window's, so the days spread over the whole window instead
/// of sitting in a 420 px strip in the middle of it; the height comes from the viewBox and the page's
/// CSS clamps it.
/// The legend is one entry per current worker type, in the type's own name: accounting's model column
/// has held both the model that ran and, since the type column arrived, the type's name, so the same
/// workers would otherwise be listed twice and share a colour. A run the accounting never named a
/// model for is listed as "other", and the runs line has its own entry, so no colour in the legend is
/// without a label (1052-C).
/// 1052-C (the developer, 2026-11-21): bars and line share one scale - the axis top is the taller of
/// the most cards and the most runs any day reached - so a day's height means the same thing for the
/// line as it does for the bars, and the number beside the axis applies to both.
/// </summary>
public static class TrendChart
{
    // The drawing box, 3:1. The page gives it the window's full width and the height the viewBox's
    // ratio implies, so every day slot takes an equal share of the window (963-C).
    private const int Width = 720;
    private const int Height = 240;
    private const int Left = 46; // room for the count that sits beside the axis
    private const int Right = 712;
    private const int Top = 14;
    private const int Baseline = 206;
    private const int DayLabel = 228;
    private const double BarFill = 0.72; // a bar takes most of its day slot (963-C)

    // The theme's own colours, in the order the legend's entries take them.
    private static readonly string[] Palette = ["var(--ok)", "var(--warn)", "var(--accent)", "var(--bad)", "var(--muted)", "var(--text)"];

    // What the runs line draws with, and what its legend chip shows (1052-C), so the key beside the
    // chart is the colour of the line and cannot drift from it.
    private const string RunsColour = "var(--text)";

    // What a run the accounting never named a model for is called in the legend (1052-C): the rows
    // written before the model column (ACCT-4) reach here with no key and no short name, and used to
    // draw a colour swatch with nothing beside it.
    private const string OtherLabel = "other";

    // What the free type's work went by before worker types existed, and what a type migrated from
    // the old slots was called before it was renamed (963-C): runs written under those names count
    // under the type that does that work today.
    private static readonly string[] FreeTypeNames = ["worker", "default", "free"];

    /// <summary>
    /// 1327-C: the chart's frame with one muted line in place of bars, for a range in which the
    /// project finished nothing (for example the last hour when the last card finished two hours ago).
    /// </summary>
    /// <param name="rangeLabel">The range as the window names it, such as "1 hour".</param>
    public static string EmptyHtml(string rangeLabel) =>
        "<div class='trend'><div class='mute trendempty'>nothing finished in the last " +
        System.Net.WebUtility.HtmlEncode(rangeLabel) + "</div></div>";

    /// <summary>
    /// The chart and its legend, or "" when there is nothing to draw. A day that finished nothing
    /// gets no bar - the bars are cards done - but its slot stays open rather than closing up, so the
    /// idle days show, and its runs still mark the line.
    /// </summary>
    /// <param name="days">The window's days, oldest first.</param>
    /// <param name="types">
    /// The current worker types (WT-P3), which give the legend its one entry per type: the model a
    /// type runs today is how a run from before the types were grouped finds it.
    /// </param>
    public static string Html(IReadOnlyList<TrendDay> days, IReadOnlyList<PageWorkerTypeView>? types = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            return "";
        }

        var legend = new Legend();
        foreach (var segment in days.SelectMany(day => day.Segments))
        {
            legend.Add(segment, types);
        }

        legend.Order();
        var groups = legend.Groups;

        // One scale for the whole chart (1052-C): the top of the axis is the taller of the most cards
        // any day finished and the most runs any day took, and both the bars and the runs line are
        // measured against it. Scaling the line to its own maximum made a day's height mean something
        // different for the line than for the bars, and the number beside the axis did not apply to
        // the line at all.
        var axisTop = Math.Max(1, Math.Max(days.Max(day => day.CardsDone), days.Max(day => day.Runs)));
        var slot = (Right - Left) / (double)days.Count;
        var bar = Math.Clamp(slot * BarFill, 3d, 72d);

        var sb = new StringBuilder();
        sb.Append("<div class='trend'>")
          .Append("<svg class='trendchart' viewBox='0 0 ").Append(Width).Append(' ').Append(Height)
          .Append("' width='100%' role='img'")
          .Append(days[0].Label.Length > 0 && days[0].Label.Contains(':') ? " aria-label='Cards finished and runs per period over the last " : " aria-label='Cards finished and runs per day for the last ").Append(days.Count)
          .Append(days[0].Label.Length > 0 && days[0].Label.Contains(':') ? " periods'" : " days'")
          .Append(" preserveAspectRatio='xMidYMid meet'>");

        // The two axes, and the count the tallest bar reaches beside the top of the vertical one
        // (963-C: it used to float at the far left of the box, away from the axis it labels).
        sb.Append("<line class='taxis' x1='").Append(Left).Append("' y1='").Append(Top)
          .Append("' x2='").Append(Left).Append("' y2='").Append(Baseline)
          .Append("' stroke='var(--line)' stroke-width='1'/>");
        sb.Append("<line class='tbase' x1='").Append(Left).Append("' y1='").Append(Baseline)
          .Append("' x2='").Append(Right).Append("' y2='").Append(Baseline)
          .Append("' stroke='var(--line)' stroke-width='1'/>");
        sb.Append("<text class='tmax' x='").Append(Left - 8).Append("' y='").Append(Top + 4)
          .Append("' text-anchor='end'>").Append(N(axisTop)).Append("</text>");
        sb.Append("<text class='tzero' x='").Append(Left - 8).Append("' y='").Append(Baseline + 13)
          .Append("' text-anchor='end'>0</text>");

        // One column per day: the types' bars stacked from the baseline up.
        var points = new StringBuilder();
        var cards = new int[groups.Count];
        for (var day = 0; day < days.Count; day++)
        {
            var centre = Left + (slot * (day + 0.5));
            var x = centre - (bar / 2d);
            var top = (double)Baseline;
            Array.Clear(cards, 0, cards.Length);
            foreach (var segment in days[day].Segments)
            {
                cards[legend.Of(segment.Model).Index] += segment.CardsDone;
            }

            foreach (var group in groups)
            {
                if (cards[group.Index] <= 0)
                {
                    continue;
                }

                var height = (cards[group.Index] / (double)axisTop) * (Baseline - Top);
                top -= height;
                sb.Append("<rect class='tbar' x='").Append(N(x)).Append("' y='").Append(N(top))
                  .Append("' width='").Append(N(bar)).Append("' height='").Append(N(height))
                  .Append("' fill='").Append(Colour(group)).Append("'/>");
            }

            // The day's runs, as one point of the thin line, on the same scale as the bars.
            var y = Baseline - (days[day].Runs / (double)axisTop) * (Baseline - Top);
            if (points.Length > 0)
            {
                points.Append(' ');
            }

            points.Append(N(centre)).Append(',').Append(N(y));

            // The date along the bottom. Every day keeps one; the page hides alternate ones when the
            // window is too narrow for them all (963-C).
            sb.Append("<text class='tday").Append(day % 2 == 1 ? " alt" : string.Empty)
              .Append("' x='").Append(N(centre)).Append("' y='").Append(DayLabel)
              .Append("' text-anchor='middle'>")
              .Append(days[day].Label.Length > 0 ? Enc(days[day].Label) : days[day].Date.ToString("MM-dd", CultureInfo.InvariantCulture)).Append("</text>");
        }

        // The runs: a thin line over the bars, on the bars' own scale (1052-C).
        sb.Append("<polyline class='truns' fill='none' stroke='").Append(RunsColour).Append("' stroke-width='1.5' opacity='.5' points='")
          .Append(points).Append("'/>");
        sb.Append("</svg>");

        // What each colour is: one entry per worker type, in the type's name (963-C), and one for the
        // runs line in the colour it draws with (1052-C), so no colour in the legend is left without
        // a label.
        sb.Append("<div class='trendlegend'>");
        foreach (var group in groups)
        {
            sb.Append("<span class='tswatch' style='color: ").Append(Colour(group))
              .Append("'>").Append(Enc(group.Label)).Append("</span>");
        }

        sb.Append("<span class='tswatch truns' style='color: ").Append(RunsColour).Append("'>runs</span>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    // The theme colour an entry draws with: its position in the legend, so two entries never share
    // one while the legend is no longer than the palette (963-C).
    private static string Colour(Group group) => Palette[group.Index % Palette.Length];

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    // A drawing number: short, and dot-separated whatever the reader's locale.
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    // The legend's entries: one per worker type the window's runs belong to, plus one per model no
    // current type runs. The order fixes which theme colour each takes.
    private sealed class Legend
    {
        private readonly Dictionary<string, Group> byModel = new(StringComparer.OrdinalIgnoreCase);

        public List<Group> Groups { get; } = [];

        // The entry a run's model key belongs to. A key seen for the first time joins the legend
        // under the type that claims it, or under its own name when none does.
        public void Add(TrendSegment segment, IReadOnlyList<PageWorkerTypeView>? types)
        {
            if (this.byModel.TryGetValue(segment.Model, out var known))
            {
                return;
            }

            var typeName = TypeNameFor(segment.Model, types);
            var key = typeName ?? segment.Model;
            var group = Groups.Find(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                // Every swatch in the legend is named (1052-C): a run whose accounting row carried no
                // model at all - the file's own before the model column (ACCT-4) - has neither a key
                // nor a short name, and used to draw a colour with nothing beside it. A row that named
                // its model but has no short name is called by that name.
                var label = typeName ?? segment.Label;
                if (string.IsNullOrWhiteSpace(label))
                {
                    label = string.IsNullOrWhiteSpace(segment.Model) ? OtherLabel : segment.Model;
                }

                group = new Group { Key = key, Label = label };
                Groups.Add(group);
            }

            this.byModel[segment.Model] = group;
        }

        public Group Of(string model) => this.byModel[model];

        // Called once the legend is complete: the entries go in name order, and their position is
        // the palette colour they will draw with.
        public void Order()
        {
            Groups.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
            for (var at = 0; at < Groups.Count; at++)
            {
                Groups[at].Index = at;
            }
        }

        // The current type a run's model key counts under (963-C): the type named it, the type that
        // runs it as its model, a type it was renamed from, or - for the names the free type's work
        // went by before the types existed - the default type (1146-A). Null when no type
        // claims the key, which leaves the run's own name in the legend.
        private static string? TypeNameFor(string model, IReadOnlyList<PageWorkerTypeView>? types)
        {
            if (types is null || types.Count == 0 || string.IsNullOrWhiteSpace(model))
            {
                return null;
            }

            foreach (var type in types)
            {
                if (Same(type.Name, model)
                    || (!string.IsNullOrWhiteSpace(type.Model) && Same(type.Model, model))
                    || (!string.IsNullOrWhiteSpace(type.OriginalName) && Same(type.OriginalName, model)))
                {
                    return type.Name;
                }
            }

            PageWorkerTypeView? free = null;
            var freeTypes = 0;
            foreach (var type in types)
            {
                if (type.IsDefault)
                {
                    free = type;
                    freeTypes++;
                }
            }

            return freeTypes == 1 && Array.Exists(FreeTypeNames, word => Same(word, model)) ? free!.Name : null;
        }
    }

    // One legend entry: the worker type (or the model, for a run no current type claims), and the
    // position whose theme colour it draws with.
    private sealed class Group
    {
        public required string Key { get; init; }

        public required string Label { get; init; }

        public int Index { get; set; }
    }
}
