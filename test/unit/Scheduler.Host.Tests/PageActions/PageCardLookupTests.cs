using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>Job card 1113-C: a page link names a card by file name or short id, open or finished.</summary>
public sealed class PageCardLookupTests
{
    private static Card Open(string id, string name) => new()
    {
        Folder = CardFolder.Active,
        Path = "jobs/active/" + name + ".md",
        Name = name,
        Num = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture),
        Category = id.Split('-')[1],
        Id = id,
        Title = id + ": open",
    };

    private static DoneCard Done(string id, string name, string folder = "done") => new()
    {
        Folder = folder,
        Path = "jobs/" + folder + "/" + name + ".md",
        Name = name,
        Num = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture),
        Category = id.Split('-')[1],
        Id = id,
        Title = id + ": finished",
    };

    [Fact]
    public void ShortId_FindsAnOpenCard()
    {
        var found = PageCardLookup.Find([Open("1113-C", "1113-C-plan-page")], [], "1113-c");

        Assert.Equal("jobs/active/1113-C-plan-page.md", found?.Path);
    }

    [Fact]
    public void ShortId_FindsADoneAndAnArchivedCard()
    {
        var done = new[] { Done("1091-C", "1091-C-plans", "done"), Done("500-A", "500-A-old", "archive") };

        Assert.Equal("jobs/done/1091-C-plans.md", PageCardLookup.Find([], done, "1091-C")?.Path);
        Assert.Equal("jobs/archive/500-A-old.md", PageCardLookup.Find([], done, "500-A-old.md")?.Path);
    }

    [Fact]
    public void UnknownOrBlank_IsNull()
    {
        Assert.Null(PageCardLookup.Find([Open("1-A", "1-A-x")], [Done("2-A", "2-A-y")], "3-A"));
        Assert.Null(PageCardLookup.Find([], [], " "));
    }
}
