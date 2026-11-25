// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// MP-P8 (multi-project design section 4): in a pool every route that names a card or a slot takes
/// <c>project=</c> and goes to that project's own data and actions; without it, it is refused
/// ("which project?"), except the pool-wide routes. One project ignores <c>project=</c> (MP-C9).
/// </summary>
public sealed class PoolRoutesTests : IDisposable
{
    private readonly FakePageData _poolData = new();
    private readonly FakePageActions _poolWide = new();
    private readonly PoolData _data;
    private readonly PoolActions _actions;
    private readonly PageActionQueue _queue = new();
    private readonly PoolRenderer _renderer = new();
    private readonly PageRouter _router;

    public PoolRoutesTests()
    {
        _data = new PoolData(_poolData);
        _actions = new PoolActions();
        foreach (var name in new[] { "AmigaX", "ProjectToolkit" })
        {
            _data.Projects[name] = new FakePageData();
            _data.Projects[name].Cards["120-A-dispatch.md"] = "card of " + name;
            _actions.Projects[name] = new FakePageActions();
        }

        _router = new PageRouter(
            _poolData,
            _poolWide,
            new FakePageRenderer(),
            new LiveFeed(new TestClock(), _poolData),
            remoteControl: false,
            _queue,
            new PoolRouting(_data, _actions, _renderer));
    }

    public void Dispose() => _queue.Dispose();

    // VW-2: the address's view reaches the pool page's renderer; with none, it is told none.
    [Theory]
    [InlineData("view=plans", "plans")]
    [InlineData("view=nonsense", "nonsense")]
    [InlineData("", null)]
    public void VW_2_ViewQuery_ReachesThePoolRenderer(string query, string? expected)
    {
        var response = _router.Handle(Request("GET", "/", query));

        Assert.Equal(200, response.Status);
        Assert.Equal(expected, _renderer.LastView);
    }

    // MP-P8: a per-project route without project= is refused, GET or POST, and nothing runs.
    [Theory]
    [InlineData("POST", "/done", "card=120-A-dispatch.md")]
    [InlineData("POST", "/stop", "slot=1")]
    [InlineData("POST", "/answer", "card=120-A-dispatch.md&q=1&a=0")]
    [InlineData("GET", "/card", "name=120-A-dispatch.md")]
    [InlineData("GET", "/log", "file=010-A-20261118-093012.log")]
    public void MP_P8_PerProjectRoute_WithoutProject_Refused(string method, string path, string query)
    {
        var response = _router.Handle(Request(method, path, query));

        Assert.Equal(400, response.Status);
        Assert.StartsWith("which project?", response.Body, StringComparison.Ordinal);
        Assert.True(_queue.WaitIdle(TimeSpan.FromSeconds(10)));
        Assert.All(_actions.Projects.Values, a => Assert.Empty(a.Calls));
        Assert.Empty(_poolWide.Calls);
    }

    // MP-P8: a project the pool does not have is refused the same way.
    [Fact]
    public void MP_P8_UnknownProject_Refused()
    {
        var response = _router.Handle(Request("POST", "/done", "project=Nope&card=120-A-dispatch.md"));

        Assert.Equal(400, response.Status);
        Assert.Contains("Nope", response.Body, StringComparison.Ordinal);
    }

    // MP-P8: with project=, from the query or from the form, the route reaches that project only.
    [Fact]
    public void MP_P8_PerProjectRoute_GoesToThatProjectOnly()
    {
        var done = _router.Handle(Request("POST", "/done", "project=amigax&card=120-A-dispatch.md"));
        var stop = _router.Handle(Request("POST", "/stop", "", form: "project=ProjectToolkit&slot=2"));
        var card = _router.Handle(Request("GET", "/card", "project=ProjectToolkit&name=120-A-dispatch.md"));
        Assert.True(_queue.WaitIdle(TimeSpan.FromSeconds(10)));

        Assert.NotEqual(400, done.Status);
        Assert.NotEqual(400, stop.Status);
        Assert.Equal("card of ProjectToolkit", card.Body);
        Assert.Equal("120-A-dispatch.md", _actions.Projects["AmigaX"].LastCard);
        Assert.Equal(0, _actions.Projects["ProjectToolkit"].CallCount("/done"));
        Assert.Equal(2, _actions.Projects["ProjectToolkit"].LastSlot);
        Assert.Equal(0, _actions.Projects["AmigaX"].CallCount("/stop"));
        Assert.Empty(_poolWide.Calls);
    }

    // 1004-C (18:49 items 10 and 11): the targets of the pool page's Recent events links - the card's
    // file view and the run's Log - are served from the project the link names, not another one.
    [Fact]
    public void Card1004_EventCardAndLogLinks_ServeThatProjectsFiles()
    {
        const string log = "010-A-20261118-093012.log";
        var root = Scheduler.TestSupport.TestTmp.NewPath("p1004");
        try
        {
            foreach (var name in _data.Projects.Keys)
            {
                var dir = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
                File.WriteAllText(Path.Combine(dir, log), "log of " + name);
                _data.Projects[name].LogDirectory = dir;
            }

            foreach (var name in new[] { "AmigaX", "ProjectToolkit" })
            {
                var card = _router.Handle(Request("GET", "/card", "project=" + name + "&name=120-A-dispatch.md"));
                var run = _router.Handle(Request("GET", "/log", "project=" + name + "&file=" + log));

                Assert.Equal("card of " + name, card.Body);
                Assert.Equal(200, run.Status);
                Assert.Contains("log of " + name, run.Body, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // MP-P8, MP-H8: the pool-wide routes need no project; Pause / Drain / Resume act on the one named,
    // or on every project without one; / draws the pool page.
    [Fact]
    public void MP_P8_PoolWideRoutes_NoProjectNeeded()
    {
        Assert.NotEqual(400, _router.Handle(Request("POST", "/pause", "")).Status);
        Assert.NotEqual(400, _router.Handle(Request("POST", "/drain", "", form: "project=AmigaX")).Status);
        Assert.NotEqual(400, _router.Handle(Request("POST", "/resume", "", form: "project=")).Status);
        Assert.NotEqual(400, _router.Handle(Request("POST", "/tick", "")).Status);
        Assert.NotEqual(400, _router.Handle(Request("POST", "/stopall", "")).Status);
        var page = _router.Handle(Request("GET", "/", ""));
        Assert.True(_queue.WaitIdle(TimeSpan.FromSeconds(10)));

        Assert.Equal(["pause:*", "drain:AmigaX", "resume:*", "stopall"], _actions.Log);
        Assert.Equal(1, _poolWide.CallCount("/tick"));
        Assert.Equal("the pool page", page.Body);
    }

    // 1004-C: a row's Stop names its project and goes to that project alone; without one it is refused,
    // and it is never the pool's Stop all and exit.
    [Fact]
    public void Card1004_StopProject_NeedsAProject_StopsThatOneOnly()
    {
        var refused = _router.Handle(Request("POST", "/stop-project", ""));
        var stop = _router.Handle(Request("POST", "/stop-project", "", form: "project=AmigaX"));
        Assert.True(_queue.WaitIdle(TimeSpan.FromSeconds(10)));

        Assert.Equal(400, refused.Status);
        Assert.Contains("which project?", refused.Body, StringComparison.Ordinal);
        Assert.NotEqual(400, stop.Status);
        Assert.Equal(["stop:AmigaX"], _actions.Log);
    }

    // 1033-C: a row's Workspaces box names its project and goes to that project alone; without one it is refused.
    [Fact]
    public void Card1033_Workspaces_NeedsAProject_SetsThatOneOnly()
    {
        var refused = _router.Handle(Request("POST", "/workspaces", "", form: "workspaces=3"));
        var set = _router.Handle(Request("POST", "/workspaces", "", form: "project=AmigaX&workspaces=3"));
        Assert.True(_queue.WaitIdle(TimeSpan.FromSeconds(10)));

        Assert.Equal(400, refused.Status);
        Assert.NotEqual(400, set.Status);
        Assert.Equal(["workspaces:AmigaX=3"], _actions.Log);
    }

    // MP-C9: one project, no pool part, ignores project= and answers as today.
    [Fact]
    public void MP_P8_OneProject_IgnoresProject()
    {
        var data = new FakePageData();
        var actions = new FakePageActions();
        using var queue = new PageActionQueue();
        var router = new PageRouter(data, actions, new FakePageRenderer(), new LiveFeed(new TestClock(), data), false, queue);

        var plain = router.Handle(Request("POST", "/done", "card=120-A-dispatch.md"));
        var named = router.Handle(Request("POST", "/retry", "project=Anything&card=120-A-dispatch.md"));
        Assert.True(queue.WaitIdle(TimeSpan.FromSeconds(10)));

        Assert.NotEqual(400, plain.Status);
        Assert.NotEqual(400, named.Status);
        Assert.Equal(1, actions.CallCount("/done"));
        Assert.Equal(1, actions.CallCount("/retry"));
    }

    private static PageRequest Request(string method, string path, string query, string form = "") => new()
    {
        Method = method,
        Path = path,
        QueryValues = PageRequest.ParseQuery(query),
        FormValues = PageRequest.ParseForm(form),
        Body = form,
    };

    private sealed class PoolData(FakePageData pool) : IPoolPageData
    {
        public Dictionary<string, FakePageData> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? FontPath => pool.FontPath;

        public string LogDirectory => pool.LogDirectory;

        public PoolPageSnapshot PoolSnapshot() => new() { Built = DateTimeOffset.UnixEpoch };

        public IPageData? For(string project) => Projects.GetValueOrDefault(project);

        public PageSnapshot Snapshot() => pool.Snapshot();

        public string? Card(string name) => pool.Card(name);

        public string? Request(string name) => pool.Request(name);

        public string? HealthText() => pool.HealthText();

        public string StatusJson() => pool.StatusJson();

        public IReadOnlyList<LiveWorker> RunningWorkers() => pool.RunningWorkers();
    }

    private sealed class PoolActions : IPoolPageActions
    {
        public Dictionary<string, FakePageActions> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Log { get; } = [];

        IReadOnlyList<string> IPoolPageActions.Projects => [.. Projects.Keys];

        public IPageActions? For(string project) => Projects.GetValueOrDefault(project);

        public ActionResult Pause(string? project) => Note("pause:" + (project ?? "*"));

        public ActionResult Drain(string? project) => Note("drain:" + (project ?? "*"));

        public ActionResult Resume(string? project) => Note("resume:" + (project ?? "*"));

        public ActionResult StopAll() => Note("stopall");

        public ActionResult Stop(string project) => Note("stop:" + project);

        public ActionResult SetWorkspaces(string project, string value) => Note("workspaces:" + project + "=" + value);

        private ActionResult Note(string what)
        {
            lock (Log)
            {
                Log.Add(what);
            }

            return new ActionResult { Ok = true };
        }
    }

    private sealed class PoolRenderer : IPoolPageRenderer
    {
        public string? LastView { get; private set; }

        public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed) => "the pool page";

        public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed, string? view)
        {
            LastView = view;
            return "the pool page";
        }
    }
}
