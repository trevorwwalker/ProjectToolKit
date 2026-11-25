// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.PageActions;
using Scheduler.Host.Processes;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Program;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using Scheduler.Host.Tick;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 949-B (the developer, 2026-11-19: "944-D won't go away"). The card had been flagged by 395-B's
/// <see cref="FinishedCardCheck"/> - its worker moved it to <c>review/</c> without changing its text -
/// and the page kept listing it under Waiting on you with a Done button after Done had moved it to
/// <c>done/</c>; the next click was refused with "944-D is not in review" (MOVE-6). The row's own
/// rule (410-A) is that it leaves when main moves on: another folder, or a new blob. That needs the
/// flag to carry the folder and blob it was raised at, and the card to carry the blob main has for
/// it now - both Host's part of the wiring.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5), moves.md (MOVE-6).
/// </summary>
public sealed class PageReviewRefreshTests : IDisposable
{
    private const string Jobs = "docs/jobs";
    private const string CardName = "944-D-spec-note";
    private const string ReviewPath = Jobs + "/review/944-D-spec-note.md";
    private const string ActivePath = Jobs + "/active/944-D-spec-note.md";
    private const string DonePath = Jobs + "/done/944-D-spec-note.md";
    private const string CardText = "# Spec note\n\nStatus: review\n\n## Result\n\n- superseded, nothing to write\n";

    private readonly List<string> _notices = [];
    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 19, 13, 36, 0, TimeSpan.Zero));
    private readonly ListLog _log = new();
    private readonly InMemoryMain _main = new();
    private readonly ActionTickHost _host = new();
    private readonly ActionProcesses _processes = new();
    private readonly FinishedCardCheck _finished;
    private readonly SchedulerTick _tick;
    private readonly HostPageData _data;
    private readonly PageServer _server;
    private readonly HttpClient _client;

    public PageReviewRefreshTests()
    {
        var logDir = Path.Combine(_temp.Path, "logs");
        Directory.CreateDirectory(logDir);
        var config = new SchedulerConfig
        {
            Project = "ptk-949b",
            JobsDir = Jobs,
            LogDir = logDir,
            Repo = _temp.Path,
            WorkspaceRoot = Path.Combine(_temp.Path, "ws"),
            Slots = 2,
            PageStopGraceMinutes = 5,
            PollSeconds = 1,
            Agents = new Dictionary<string, AgentDefinition>(),
        };
        var files = new StateFiles(logDir);
        _finished = new FinishedCardCheck(_main, Jobs);

        // The worker's run: the card was in active/ with this text, then moved to review/ unchanged,
        // which is what 395-B flags (scheduler.log, 12:50:09: "944-D...: finished without updating the card").
        _main.Files[ActivePath] = CardText;
        _finished.Observe(CardName);
        _main.Files.Remove(ActivePath);
        _main.Files[ReviewPath] = CardText;
        Assert.Equal(FinishedCardCheck.Note, _finished.Check(CardName, "review"));

        _host.ReadCardsFrom = ReadCards;
        _tick = new SchedulerTick(config, _clock, _log, new TickLoad(), new TickProbe(), _processes, files, _host);
        _data = new HostPageData(
            config,
            _clock,
            _tick,
            _main,
            files,
            () => null,
            new PageSnapshotSources { FinishedWithoutUpdate = () => _finished.Flagged });

        var actions = new HostPageActions(new PageActionParts
        {
            Config = config,
            Clock = _clock,
            Log = _log,
            Files = files,
            Tick = _tick,
            Processes = _processes,
            Committer = new MovingCommitter(_main),
            Main = _main,
            Notice = message => _notices.Add(message),
            // What the loop does after a click asks for one (PAGE-4): the tick, and the snapshot with it.
            TickNow = () =>
            {
                _tick.Run();
                _data.Refresh();
            },
        });

        var feed = new LiveFeed(_clock, _data);
        _server = new PageServer(new PageServerOptions
        {
            Port = 0,
            RemoteControl = true,
            Data = _data,
            Actions = actions,
            Renderer = new PageRenderer(),
            Feed = feed,
        });
        _server.Start();
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        // One tick as the loop's first: the tick reads main, and the snapshot is built from it.
        _tick.Run();
        _data.Refresh();
    }

    // MOVE-6, PAGE-5 (949-B): after a Done from the page the card is gone from the page - the next
    // snapshot leaves it out of Waiting on you, and the next live poll carries a new page version.
    [Fact]
    public async Task Page_DoneFromPage_NextPollNoLongerListsTheCard()
    {
        var before = await Get("/");
        Assert.Contains(CardName, before, StringComparison.Ordinal);
        Assert.Contains(">stuck<", before, StringComparison.Ordinal);
        var versionBefore = PageVersion.Of(_data.Snapshot());

        var posted = await _client.PostAsync("/done?card=" + CardName, new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.SeeOther, posted.StatusCode);

        // 957-B: the reply goes out before the commit; wait for the queue's one worker to finish it.
        Assert.True(_server.Queue.WaitIdle(TimeSpan.FromSeconds(10)), "the Done from the page never ran");
        Assert.True(_main.Files.ContainsKey(DonePath), "the Done commit did not move the card to done/");
        Assert.False(_main.Files.ContainsKey(ReviewPath), "the Done commit left the review/ copy");
        Assert.DoesNotContain("is not in review", string.Join('\n', _notices), StringComparison.Ordinal);

        var after = await Get("/");
        Assert.DoesNotContain(CardName, after, StringComparison.Ordinal);

        var live = await Get("/live.json");
        var versionAfter = PageVersion.Of(_data.Snapshot());
        Assert.NotEqual(versionBefore, versionAfter);
        Assert.Contains($"\"page\":\"{versionAfter}\"", live, StringComparison.Ordinal);
    }

    // PAGE-5 (410-A): the row shows while main still has the card where it was flagged, with the text
    // it was flagged at - so the developer is still asked to fill the Result in and press Done.
    [Fact]
    public void Snapshot_FlaggedCard_ShownWhileMainStillSaysTheSame()
    {
        var row = Assert.Single(_data.Snapshot().Waiting, w => w.Kind == PageWaitingKind.FinishedWithoutUpdate);
        Assert.Equal(CardName, row.Name);
        Assert.Equal(PageWaiting.FinishedWithoutUpdateNote, row.Note);
    }

    // PAGE-5 (410-A): a fresh commit that rewrites the card - the developer filling its Result in -
    // is a new blob, which is the other way the row leaves. Needs Card.BlobId from the tree (CARD-1).
    [Fact]
    public void Snapshot_FlaggedCard_GoneWhenTheCardIsRewritten()
    {
        _main.Files[ReviewPath] = CardText + "\nThe Result, written after the flag.\n";
        _tick.Run();
        _data.Refresh();

        Assert.DoesNotContain(
            _data.Snapshot().Waiting,
            w => w.Kind == PageWaitingKind.FinishedWithoutUpdate);
    }

    // The flag names the folder and blob main showed when it was raised; without them no page action
    // could ever clear the row (410-A's wiring, the cause of 949-B).
    [Fact]
    public void FinishedCardCheck_Flag_NamesTheFolderAndBlobItWasRaisedAt()
    {
        var flag = Assert.Single(_finished.Flagged);
        Assert.Equal(CardName, flag.Card);
        Assert.Equal(CardFolder.Review, flag.Folder);
        Assert.Equal(Blob(CardText), flag.Stamp);
    }

    private async Task<string> Get(string path)
    {
        var response = await _client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>What <see cref="CardReader"/> hands the tick: every card main has, with the blob the tree gives it.</summary>
    private IReadOnlyList<Card> ReadCards()
    {
        var cards = new List<Card>();
        foreach (var (path, text) in _main.Files)
        {
            if (CardFiles.Parse(Jobs, path, text) is { } card)
            {
                cards.Add(card with { BlobId = Blob(text) });
            }
        }

        return cards;
    }

    private static string Blob(string text) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Main as the page sees it: files in memory, read through the same ports the host uses.</summary>
    internal sealed class InMemoryMain : IMainRepository
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public bool Fetch() => true;

        public IReadOnlyList<TreeEntry>? ListTree(string folder)
        {
            var prefix = folder.TrimEnd('/') + "/";
            return [.. Files.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(f => new TreeEntry { Path = f.Key, BlobId = Blob(f.Value) })];
        }

        public string? ReadBlob(string blobId) => Files.FirstOrDefault(f => Blob(f.Value) == blobId).Value;

        public string? ReadFile(string path) => Files.TryGetValue(path, out var text) ? text : null;

        public string? HeadSha() => "abc1234";
    }

    /// <summary>The answers worktree: a page action's change becomes a commit that moves the card.</summary>
    internal sealed class MovingCommitter(InMemoryMain main) : IJobsCommitter
    {
        public List<string> Messages { get; } = [];

        public CommitResult Apply(JobsChange change)
        {
            Messages.Add(change.Message);

            // A commit's worth of changes, in the order JobsChange lists them: moves, writes, removals.
            foreach (var move in change.Moves)
            {
                if (main.Files.TryGetValue(move.From, out var text) && !main.Files.ContainsKey(move.To))
                {
                    main.Files[move.To] = text;
                }

                main.Files.Remove(move.From);
            }

            foreach (var (path, text) in change.Writes)
            {
                main.Files[path] = text;
            }

            foreach (var path in change.Removals)
            {
                main.Files.Remove(path);
            }

            return new CommitResult { Ok = true, Sha = "abc1234" };
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        _temp.Dispose();
    }
}
