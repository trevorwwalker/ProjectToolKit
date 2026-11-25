// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1013-B: a cold start reads every request and card blob it needs in one <c>git cat-file --batch</c>, not one
/// git process each (CARD-1, CARD-18): AmigaX's 150 request files took 83 s one process at a time.
/// </summary>
public class ColdStartReadTests
{
    private const string Jobs = "jobs";

    [Fact]
    public void Requests_ColdStart_150Files_OneBlobProcess()
    {
        using var fx = new GitFixture();
        var files = new Dictionary<string, string?>();
        for (var i = 1; i <= 150; i++)
        {
            files[$"jobs/requests/{i:000}-r.md"] = $"# Request {i}\n\nStatus: {(i % 2 == 0 ? "answered" : "open")}\nFor: 1-B-x\n";
        }

        files["jobs/requests/answered/900-old.md"] = "# Old\n";
        fx.Push("requests", files);
        var runner = new GitRunner();
        var main = fx.Main(runner);
        Assert.True(main.Fetch());
        var before = runner.Started;

        var requests = new RequestReader(main, Jobs).Read();

        Assert.Equal(150, requests.Count);
        Assert.Equal("Request 150", requests[^1].Title);
        Assert.Equal(75, requests.Count(r => r.Open));
        // The count is the proof of one batch; a time limit here only failed under load (1275-B).
        Assert.Equal(3, runner.Started - before);   // rev-parse, ls-tree, one cat-file --batch
    }

    [Fact]
    public void Cards_ColdStart_OneBlobProcess_DoneNeverOpened()
    {
        using var fx = new GitFixture();
        var files = new Dictionary<string, string?>();
        for (var i = 1; i <= 60; i++)
        {
            files[$"jobs/queue/{i}-B-card.md"] = $"# {i}-B: Card\n\nCategory: B\nStatus: ready\n";
            files[$"jobs/done/{100 + i}-B-old.md"] = $"# {100 + i}-B: Old\n\nStatus: done\n";
        }

        fx.Push("cards", files);
        var runner = new GitRunner();
        var main = fx.Main(runner);
        Assert.True(main.Fetch());
        var before = runner.Started;

        var cards = new CardReader(main, Jobs, new ListLog()) { ByCommit = true }.Read();

        Assert.Equal(120, cards.Count);
        Assert.All(cards.Where(c => c.Folder == CardFolder.Queue), c => Assert.Equal("ready", c.Status));
        Assert.Equal(3, runner.Started - before);   // rev-parse, ls-tree, one cat-file --batch
    }

    [Fact]
    public void Batch_MissingBlob_IsLeftOut_AndTextIsUtf8()
    {
        var text = "# Über ✓\n\nStatus: open\n";
        var body = Encoding.UTF8.GetBytes(text);
        var output = new List<byte>();
        output.AddRange(Encoding.ASCII.GetBytes("aaaa missing\n"));
        output.AddRange(Encoding.ASCII.GetBytes($"bbbb blob {body.Length}\n"));
        output.AddRange(body);
        output.Add((byte)'\n');
        output.AddRange(Encoding.ASCII.GetBytes("cccc blob 0\n\n"));

        var parsed = BlobBatch.Parse([.. output]);

        Assert.False(parsed.ContainsKey("aaaa"));
        Assert.Equal(text, parsed["bbbb"]);
        Assert.Equal("", parsed["cccc"]);
        Assert.Equal("aaaa\nbbbb\n", Encoding.ASCII.GetString(BlobBatch.Input(["aaaa", "bbbb"])));
    }

    [Fact]
    public void Batch_UnknownBlobAmongGood_OthersStillRead()
    {
        using var fx = new GitFixture();
        fx.Push("one", new Dictionary<string, string?> { ["jobs/requests/001-a.md"] = "# A\n" });
        var main = fx.Main();
        Assert.True(main.Fetch());
        var id = main.ListTree("jobs/requests")!.Single().BlobId;

        var read = main.ReadBlobs([id, "0123456789012345678901234567890123456789"]);

        Assert.Equal("# A\n", read[id].Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Single(read);
    }
}
