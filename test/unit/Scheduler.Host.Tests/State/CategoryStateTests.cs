// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.State;

/// <summary>
/// categories-accepted.json in the log folder (LANE-2, <c>Read-Categories</c> L1630-1644): Host owns the
/// file, Core the comparison of the definitions. Test plan: dispatch.md.
/// </summary>
public class CategoryStateTests
{
    private const string Rel = "docs/jobs/lanes.json";

    // LANE-2: a category categories-accepted.json has never seen is accepted, stored and not held.
    [Fact]
    public void Categories_NewCategory_Accepted()
    {
        using var dir = new TempDir();
        var files = new StateFiles(dir.Path);
        var log = new ListLog();
        var registry = new CategoryRegistry(log, files, Rel);

        registry.Read(Categories("A", "B"), []);
        var path = Path.Combine(dir.Path, "lanes-accepted.json");
        Assert.True(File.Exists(path));

        // categories.json adds category E; categories-accepted.json has never seen it.
        registry.Read(Categories("A", "B", "E"), [CardOf("060-B-done", CardFolder.Done)]);


        // It is stored in the log folder, and a fresh Host reader reads it back.
        using var doc = JsonDocument.Parse(new StateFiles(dir.Path).Read(StateFile.CategoriesAccepted)!);
        Assert.Equal(3, doc.RootElement.EnumerateObject().Count());
        Assert.True(doc.RootElement.TryGetProperty("E", out var e));
        Assert.Contains("src/e/", e.GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(log.Lines, l => l.Contains("redefined", StringComparison.Ordinal));
    }

    // LANE-2: a changed category with no card of its own in queue/ or active/ is accepted, logged and stored.
    [Fact]
    public void Categories_ChangedNoPendingCards_AcceptedAndLogged()
    {
        using var dir = new TempDir();
        var files = new StateFiles(dir.Path);
        var log = new ListLog();
        var registry = new CategoryRegistry(log, files, Rel);
        registry.Read(CategoriesOwns("A", "src/core/"), []);
        Assert.DoesNotContain(log.Lines, l => l.Contains("redefined", StringComparison.Ordinal));

        // category A's owns changes; its only card has finished, so nothing of A's is queued or active.
        registry.Read(CategoriesOwns("A", "src/core/", "docs/extra/"), [CardOf("060-A-done", CardFolder.Done)]);

        Assert.Contains(
            "category A redefined in docs/jobs/lanes.json with no cards pending: accepted",
            log.Lines,
            StringComparer.Ordinal);
        var stored = new StateFiles(dir.Path).Read(StateFile.CategoriesAccepted)!;
        Assert.Contains("docs/extra/", stored, StringComparison.Ordinal);
    }

    private static string Categories(params string[] letters) =>
        "{\"lanes\":{" + string.Join(",", letters.Select(l => $"\"{l}\":{{\"name\":\"lane {l}\",\"owns\":[\"src/{l.ToLower(CultureInfo.InvariantCulture)}/\"]}}")) + "}}";

    private static string CategoriesOwns(string letter, params string[] owns) =>
        $"{{\"lanes\":{{\"{letter}\":{{\"name\":\"lane {letter}\",\"owns\":[{string.Join(",", owns.Select(o => $"\"{o}\""))}]}}}}}}";

    private static Card CardOf(string name, CardFolder folder) => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{name}.md",
        Name = name,
        Num = long.Parse(name[..name.IndexOf('-')], CultureInfo.InvariantCulture), // 931-A: any width
        Category = name[4..5],
        Id = name[..5],
    };
}
