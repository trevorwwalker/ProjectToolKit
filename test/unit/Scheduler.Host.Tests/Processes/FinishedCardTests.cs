using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;

namespace Scheduler.Host.Tests.Processes;

/// <summary>
/// A run that says done or review but leaves its card unchanged apart from the move (job card
/// 395-B: the worker's edit was never staged, so the move commit was a pure rename).
/// </summary>
public sealed class FinishedCardTests
{
    private const string Jobs = "docs/jobs";
    private const string Card = "200-A-claims";

    private sealed class FakeMain : IMainRepository
    {
        public Dictionary<string, string> Blobs { get; } = [];   // path -> blob id

        public bool Fetch() => true;

        public IReadOnlyList<TreeEntry>? ListTree(string folder) =>
            Blobs.Where(b => b.Key.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal))
                .Select(b => new TreeEntry { Path = b.Key, BlobId = b.Value }).ToList();

        public string? ReadBlob(string blobId) => null;

        public string? ReadFile(string path) => null;

        public string? HeadSha() => "abc";
    }

    // The card is seen in active/ while the run is live, then sits in review/ with the same blob.
    [Fact]
    public void Finish_CardMovedButTextUnchanged_FlaggedForDeveloper()
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);

        main.Blobs.Remove($"{Jobs}/active/{Card}.md");
        main.Blobs[$"{Jobs}/review/{Card}.md"] = "b1";

        Assert.Equal(FinishedCardCheck.Note, check.Check(Card, "review"));
        Assert.Equal("finished without updating the card", FinishedCardCheck.Note);
    }

    [Theory]
    [InlineData("done", "done")]
    [InlineData("review", "review")]
    public void Finish_CardMovedButTextUnchanged_FlaggedForBothFolders(string result, string folder)
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);
        main.Blobs.Remove($"{Jobs}/active/{Card}.md");
        main.Blobs[$"{Jobs}/{folder}/{Card}.md"] = "b1";

        Assert.NotNull(check.Check(Card, result));
    }

    // The move carried a new Result: a different blob.
    [Fact]
    public void Finish_CardMovedWithResult_NotFlagged()
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);
        main.Blobs.Remove($"{Jobs}/active/{Card}.md");
        main.Blobs[$"{Jobs}/review/{Card}.md"] = "b2";

        Assert.Null(check.Check(Card, "review"));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("needs-human")]
    [InlineData("stopped")]
    [InlineData("waiting")]
    public void Finish_OtherResults_NotChecked(string result)
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);

        Assert.Null(check.Check(Card, result));
    }

    // Never seen in active/ (for example an adopted worker): nothing to compare, so no flag.
    [Fact]
    public void Finish_CardNeverObserved_NotFlagged()
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/done/{Card}.md"] = "b1";

        Assert.Null(check.Check(Card, "done"));
    }

    // The check runs once per run: a second run of the same card starts clean.
    [Fact]
    public void Finish_AfterCheck_ObservationForgotten()
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);
        main.Blobs.Remove($"{Jobs}/active/{Card}.md");
        main.Blobs[$"{Jobs}/done/{Card}.md"] = "b1";
        Assert.NotNull(check.Check(Card, "done"));

        Assert.Null(check.Check(Card, "done"));
    }

    // The flag carries what main showed when it was raised (410-A): the page's row leaves once the
    // developer's Done moves the card to another folder, or a fresh commit gives it a new blob.
    [Theory]
    [InlineData("done", "done", CardFolder.Done)]
    [InlineData("review", "review", CardFolder.Review)]
    public void Finish_Flag_KeepsTheFolderAndBlob(string result, string folder, CardFolder expected)
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);
        main.Blobs.Remove($"{Jobs}/active/{Card}.md");
        main.Blobs[$"{Jobs}/{folder}/{Card}.md"] = "b1";
        Assert.NotNull(check.Check(Card, result));

        var flag = Assert.Single(check.Flagged);
        Assert.Equal(Card, flag.Card);
        Assert.Equal(expected, flag.Folder);
        Assert.Equal("b1", flag.Stamp);
    }

    // The card is still in active/ (the worker never moved it): not this check's business.
    [Fact]
    public void Finish_CardStillInActive_NotFlagged()
    {
        var main = new FakeMain();
        var check = new FinishedCardCheck(main, Jobs);
        main.Blobs[$"{Jobs}/active/{Card}.md"] = "b1";
        check.Observe(Card);

        Assert.Null(check.Check(Card, "done"));
    }
}
