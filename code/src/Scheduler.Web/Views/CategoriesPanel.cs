// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Text;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The Categories panel in the Control window (992-B), which replaced the Lanes button that opened the
/// categories file in its program. It works as the Worker types panel does (991-C): a heading that
/// shows or hides it - hidden by default, remembered in the browser - with a summary line of the
/// categories as their coloured chips (970-C) while hidden. Each row is a category: letter, short name,
/// purpose, colour, and its owns and except lists, one path or glob per line (961-A); under them the
/// shared add-only list; Add category, Remove and Save, which posts the whole panel to
/// <c>/categories</c>. The page script's Add, Remove and toggle handlers serve both panels.
/// </summary>
internal static class CategoriesPanel
{
    private static readonly PageCategory Blank = new() { Letter = "" };

    /// <summary>The panel's HTML.</summary>
    public static string Html(PageSnapshot snapshot, bool controlsAllowed)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var dis = controlsAllowed ? "" : " disabled";
        var categories = snapshot.Categories;

        var table = new StringBuilder()
            .Append("<tr><th>Letter</th><th>Name</th><th>Purpose</th><th>Colour</th><th>Owns</th><th>Except</th><th></th></tr>");
        foreach (var category in categories)
        {
            table.Append(Row(category, dis));
        }

        var chips = string.Concat(categories.Select(c => CategoryChip.Html(categories, c.Letter)));
        return new StringBuilder()
            .Append("<form method='post' action='/categories' class='wtypes wthidden cats'>")
            .Append("<div class='wtypesbar'><button type='button' class='wttoggle' data-key='cats-open'")
            .Append(" title='Show or hide the categories; remembered in this browser'>Categories <span class='wtarrow'>&#9656;</span></button> ")
            .Append("<span class='mute wtsum'>").Append(chips.Length > 0 ? chips : "none").Append("</span></div>")
            .Append("<div class='wtbody'><div class='wtypesbar'>")
            .Append("<button type='button' class='addtype' data-tpl='cat-new'").Append(dis)
            .Append(" title='Add a category to the end of the list'>Add category</button> <button").Append(dis)
            .Append(" title='Write these categories to the categories file; the change is committed and applies at once'>Save</button>")
            .Append(" <span class='mute'>One path or glob per line: a folder ends in /, * and ? match. A changed letter moves the open cards with it.</span></div>")
            .Append("<table class='wtypes'>").Append(table).Append("</table>")
            .Append("<div class='wtypesbar'><label>Shared, add only (one per line)<br><textarea name='addonly' rows='4' cols='50'").Append(dis)
            .Append(" title='Files any category may add to but not rewrite'>").Append(Enc(string.Join("\n", snapshot.SharedAddOnly)))
            .Append("</textarea></label></div></div>")
            // The row Add category copies. A template's contents are inert, so its fields post nothing.
            .Append("<template id='cat-new'>").Append(Row(Blank, dis)).Append("</template>")
            .Append("</form>")
            .ToString();
    }

    // One category: every field posts a value, so the route reads the rows as lists of the same length.
    // The letter it was loaded with travels with it, which is what tells a letter change from a remove
    // and an add; the Add category row has none, which is how a new category says it is new.
    private static string Row(PageCategory category, string dis) =>
        new StringBuilder()
            .Append("<tr class='catrow'>")
            .Append("<td><input type='hidden' name='corig' value='").Append(Enc(category.OriginalLetter.Length > 0 ? category.OriginalLetter : category.Letter)).Append("'>")
            .Append("<input name='cletter' value='").Append(Enc(category.Letter)).Append("' size='2' maxlength='1' title='One letter, A to Z'").Append(dis).Append("></td>")
            .Append("<td><input name='cname' value='").Append(Enc(category.Name)).Append("' size='8'").Append(dis).Append("></td>")
            .Append("<td><textarea name='cpurpose' rows='3' cols='30'").Append(dis).Append('>').Append(Enc(category.Purpose)).Append("</textarea></td>")
            .Append("<td><input name='ccolor' value='").Append(Enc(category.Color)).Append("' size='8' placeholder='#rrggbb'").Append(dis).Append("></td>")
            .Append("<td><textarea name='cowns' rows='3' cols='32'").Append(dis).Append('>').Append(Enc(string.Join("\n", category.Owns))).Append("</textarea></td>")
            .Append("<td><textarea name='cexcept' rows='3' cols='24'").Append(dis).Append('>').Append(Enc(string.Join("\n", category.Except))).Append("</textarea></td>")
            .Append("<td><button type='button' class='typeremove' title='Take this category out of the list; refused while open cards use it'").Append(dis)
            .Append(">Remove</button></td></tr>")
            .ToString();

    private static string Enc(string text) => WebUtility.HtmlEncode(text);
}
