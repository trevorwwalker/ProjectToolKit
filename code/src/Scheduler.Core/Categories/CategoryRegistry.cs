using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Categories;

/// <summary>
/// The categories as read from main; none is held since 560-A (LANE-1, LANE-2, LANE-4;
/// <c>Read-Categories</c> L1622). The accepted definitions are kept in categories-accepted.json through
/// <see cref="IStateFiles"/>; Host reads and writes the file, Core owns its text.
/// </summary>
public sealed class CategoryRegistry
{
    private readonly ISchedulerLog _log;
    private readonly IStateFiles _files;
    private readonly string _categoriesRel;
    private readonly Dictionary<string, string> _held = new(Text.Comparer);

    /// <summary>Categories read from <paramref name="categoriesRel"/> (for example jobs/categories.json).</summary>
    public CategoryRegistry(ISchedulerLog log, IStateFiles files, string categoriesRel)
    {
        _log = log;
        _files = files;
        _categoriesRel = categoriesRel;
    }

    /// <summary>The categories of the last good read, or null before one.</summary>
    public CategoriesFile? Current { get; private set; }

    /// <summary>
    /// The held categories and why, by letter: always empty since 560-A (SC-4, 320-A Q5: a redefined
    /// category is no longer held). Kept until the page snapshot stops passing it to the Waiting window.
    /// </summary>
    public IReadOnlyDictionary<string, string> Held => _held;

    /// <summary>The overlap warnings of the last good read (LANE-4).</summary>
    public IReadOnlyList<string> Overlaps { get; private set; } = [];

    /// <summary>The category brief for a worker's prompt (LANE-5).</summary>
    public string Brief(string letter) => CategoryBrief.For(Current, letter, _categoriesRel);

    /// <summary>
    /// Read categories.json's text from main. null (not readable) changes nothing; invalid JSON is
    /// logged and the previous categories kept; a file without a <c>categories</c> key is ignored silently.
    /// Then each category is compared with the accepted definitions: new ones are accepted, changed
    /// ones are accepted and logged, whether or not cards of the category are in queue/ or active/
    /// (LANE-2 as changed by SC-4 and 320-A Q5: nothing is held).
    /// </summary>
    public void Read(string? text, IEnumerable<Card> cards)
    {
        if (text is null)
        {
            return;
        }

        CategoriesFile? file;
        try
        {
            file = CategoriesJson.Parse(text);
        }
        catch (JsonException)
        {
            _log.Write($"{_categoriesRel} on main is not valid JSON; keeping the previous categories");
            return;
        }

        if (file is null)
        {
            return;
        }

        Current = file;
        var accepted = LoadAccepted();
        var changed = false;
        var pendingCards = cards.Where(c => c.Folder is CardFolder.Queue or CardFolder.Active).ToList();
        foreach (var letter in file.Categories.Keys.OrderBy(k => k, Text.SortComparer))
        {
            var json = file.Categories[letter].Json;
            if (!accepted.TryGetValue(letter, out var known))
            {
                accepted[letter] = json;
                changed = true;
                continue;
            }

            if (known == json)
            {
                continue;
            }

            // SC-4, 320-A Q5 (560-A): the new definition simply applies; its cards take the new scope.
            var pending = pendingCards.Count(c => Text.Same(c.Category, letter));
            accepted[letter] = json;
            changed = true;
            var cardsPending = pending == 0 ? "no cards" : $"{pending} cards";
            _log.Write($"category {letter} redefined in {_categoriesRel} with {cardsPending} pending: accepted");
        }

        if (changed)
        {
            SaveAccepted(accepted);
        }

        Overlaps = CategoryOverlaps.Find(file);
    }

    private Dictionary<string, string> LoadAccepted()
    {
        var accepted = new Dictionary<string, string>(Text.Comparer);
        var text = _files.Read(StateFile.CategoriesAccepted);
        if (string.IsNullOrWhiteSpace(text))
        {
            return accepted;
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject obj)
            {
                foreach (var property in obj)
                {
                    accepted[property.Key] = property.Value is JsonValue v && v.TryGetValue<string>(out var s)
                        ? s
                        : property.Value?.ToJsonString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            // Unreadable counts as empty, as the script's try/catch did (L1612).
        }

        return accepted;
    }

    private void SaveAccepted(Dictionary<string, string> accepted)
    {
        var obj = new JsonObject();
        foreach (var key in accepted.Keys.OrderBy(k => k, Text.SortComparer))
        {
            obj[key] = accepted[key];
        }

        _files.Write(StateFile.CategoriesAccepted, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
