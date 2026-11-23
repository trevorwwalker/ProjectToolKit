using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The review panel <c>/review?card=</c> (roadmap item 6, card 650-C): one finished card's status,
/// questions, commits, health of main, Result and files, with Done and Send back beside them.
/// </summary>
public sealed class ReviewPage(PageThemes? themes = null)
{
    private PageThemes Themes { get; } = themes ?? PageThemes.BuiltIn;

    /// <summary>The page for <paramref name="panel"/>.</summary>
    public string Render(ReviewPanel panel, PageHealthView health, bool controlsAllowed)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(health);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'>")
          .Append("<meta name='viewport' content='width=device-width, initial-scale=1'>")
          .Append(Scheduler.Web.Server.PageFavicon.Link)
          .Append("<title>").Append(Enc(panel.Name)).Append(" review</title>")
          .Append(Themes.Head())
          .Append("</head><body><div class='desk'><div class='col' id='review'>");

        var card = Url(panel.Name);
        Window(sb, "Review: " + (string.IsNullOrEmpty(panel.Title) ? panel.Name : panel.Title),
            "<div class='review-status'>" + Enc(panel.Status) + "</div><div class='review-actions'>"
            + Post(NoteId(panel.Name), "/done", "Done", "Reviewed: mark this card done and move it to done/. The note, if any, goes into the card's ## Dev section.", controlsAllowed)
            + Post(NoteId(panel.Name), "/resume-card", "Send back", "Not finished: back to active/, ready. The note, if any, goes into the card's ## Dev section.", controlsAllowed)
            + NoteBox(panel.Name, controlsAllowed)
            + " <a href='/'>Back to the page</a></div>");

        if (panel.Questions.Count > 0)
        {
            Window(sb, "Questions", string.Concat(panel.Questions.Select(q => Question(panel.Name, q, controlsAllowed))));
        }

        Window(sb, "Commits", Commits(panel.Commits));
        Window(sb, "Health of main", Health(health, panel));
        Window(sb, "Result", string.IsNullOrWhiteSpace(panel.Result)
            ? "<span class='mute'>The card has no Result yet.</span>"
            : ReviewMarkdown.ToHtml(panel.Result));
        if (panel.Links.Count > 0)
        {
            Window(sb, "Files the Result names", "<ul>" + string.Concat(panel.Links.Select(p =>
                $"<li><a href='/repo-file?path={Url(p)}'>{Enc(p)}</a></li>")) + "</ul>");
        }

        return sb.Append("</div></div></body></html>").ToString();
    }

    private static string Question(string card, CardQuestion q, bool allowed)
    {
        var dis = allowed ? "" : " disabled";
        var id = NoteId(card);
        var buttons = new StringBuilder();
        for (var i = 0; i < q.Choices.Count; i++)
        {
            buttons.Append("<button form='").Append(id).Append("' formaction='/answer?q=").Append(q.Number).Append("' name='a' value='").Append(i).Append('\'').Append(dis).Append('>')
                   .Append(Enc(q.Choices[i].Label)).Append("</button> ");
        }

        return $"<span class='q'>Q{q.Number}: {Enc(q.Text)}</span><div>{buttons}</div>";
    }

    private static string Commits(IReadOnlyList<ReviewCommit> commits)
    {
        if (commits.Count == 0)
        {
            return "<span class='mute'>No commit names this card.</span>";
        }

        var sb = new StringBuilder("<table class='commits'><tr><th>Commit</th><th>When</th><th>Subject</th><th>Files</th></tr>");
        foreach (var c in commits)
        {
            var files = c.Files.Count == 0
                ? "<span class='mute'>none</span>"
                : string.Join("<br>", c.Files.Select(f => $"{Enc(f.Path)} <span class='ok'>+{f.Added}</span> <span class='bad'>-{f.Removed}</span>"));
            sb.Append("<tr><td><code>").Append(Enc(Short(c.Sha))).Append("</code></td><td>")
              .Append(Enc(c.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append("</td><td>")
              .Append(Enc(c.Subject)).Append("</td><td>").Append(files).Append("</td></tr>");
        }

        return sb.Append("</table>").ToString();
    }

    private static string Health(PageHealthView health, ReviewPanel panel)
    {
        var last = panel.Commits.Count > 0 ? panel.Commits[^1].Sha : "";
        var other = last.Length > 0 && health.Sha.Length > 0 && !string.Equals(last, health.Sha, StringComparison.OrdinalIgnoreCase);
        return (health.Green ? "<span class='ok'>green</span>" : "<span class='bad'>red</span>")
            + (health.Sha.Length > 0 ? " at <code>" + Enc(Short(health.Sha)) + "</code>" : " (no check has run yet)")
            + (other ? " <span class='mute'>(not the card's last commit, " + Enc(Short(last)) + ")</span>" : "")
            + (health.Text.Length > 0 ? " " + Enc(health.Text) : "");
    }

    // 1099-C: the card's one note box with its Save note button; Done, Send back and the answers above are
    // buttons of this form, so what is typed goes with whichever is pressed.
    private static string NoteId(string card) => Regex.Replace($"n-{card}", "[^A-Za-z0-9-]", "-");

    private static string NoteBox(string card, bool allowed) =>
        allowed
            ? $"<form id='{NoteId(card)}' method='post' action='/note' class='g' data-tip='Type a note, then press any button of this card to send it with that, or Save note to file it in the card&#39;s ## Dev section without moving it.'>"
              + $"<input type='hidden' name='card' value='{Enc(card)}'><input name='note' maxlength='500' placeholder='note (optional)'> <button>Save note</button></form>"
            : "";

    private static string Post(string noteForm, string action, string label, string tip, bool allowed)
    {
        if (!allowed)
        {
            return $"<span class='g' data-tip='{Enc(tip)}'><button disabled>{Enc(label)}</button></span>";
        }

        return $"<span class='g' data-tip='{Enc(tip)}'><button form='{noteForm}' formaction='{action}'>{Enc(label)}</button></span>";
    }

    private static void Window(StringBuilder sb, string title, string body) =>
        sb.Append("<section class='win'><div class='bar'><b>").Append(Enc(title))
          .Append("</b></div><div class='inner'>").Append(body).Append("</div></section>");

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string Enc(string text) => WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
