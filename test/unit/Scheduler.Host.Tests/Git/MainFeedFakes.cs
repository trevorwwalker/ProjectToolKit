// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Tests.MainModel;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1063-B: a repository's model of main as the tick reads it, over <see cref="FakeMain"/>: no git, no folder. A
/// fetch is counted (and can be made to fail); <see cref="Tick"/> is the in-process ref read (MM-8), which moves the
/// model to <see cref="FakeMain.Head"/>.
/// </summary>
internal sealed class FakeFeed : IMainFeed
{
    public FakeFeed(FakeMain main, TimeSpan? poll = null)
    {
        Main = main;
        Gate = new FetchGate(main.Clock, poll ?? TimeSpan.FromSeconds(58), () =>
        {
            var inFlight = Interlocked.Increment(ref _inFlight);
            MaxInFlight = Math.Max(MaxInFlight, inFlight);
            try
            {
                FetchStarted?.Invoke();
                Fetches++;
                if (FetchWorks)
                {
                    _fetchedHead = Main.Head;
                }

                return FetchWorks;
            }
            finally
            {
                _ = Interlocked.Decrement(ref _inFlight);
            }
        });
    }

    private int _inFlight;
    private string? _fetchedHead;

    /// <summary>1124-B: run inside each fetch, before it counts (a test makes the fetch slow with it).</summary>
    public Action? FetchStarted { get; set; }

    /// <summary>1124-B: the most fetches that ran at once.</summary>
    public int MaxInFlight { get; private set; }

    /// <summary>
    /// 1124-B: the tracking ref moves only when a fetch works (a <see cref="FakeMain.Stage"/> is then a push on the
    /// remote, seen after the next fetch); otherwise the ref read sees <see cref="FakeMain.Head"/> at once.
    /// </summary>
    public bool RefFollowsFetch { get; set; }

    public FakeMain Main { get; }

    /// <summary>The fetches the gate let run.</summary>
    public int Fetches { get; private set; }

    /// <summary>Ref reads (MM-8).</summary>
    public int RefReads { get; private set; }

    public bool FetchWorks { get; set; } = true;

    public IMainModel? Model => Main;

    public FetchGate Gate { get; }

    public bool Tick()
    {
        RefReads++;
        return Main.Model.RefRead(RefFollowsFetch ? _fetchedHead : Main.Head);
    }

    public FetchAnswer Fetch(FetchAsk why)
    {
        var answer = Gate.Ask(why);
        if (answer == FetchAnswer.Fetched)
        {
            Tick();
        }

        return answer;
    }

    /// <summary>A fake main over <paramref name="files"/>, its clock settable.</summary>
    public static FakeFeed Over(IReadOnlyDictionary<string, string> files, TimeSpan? poll = null) =>
        new(new FakeMain(files, clock: new FakeMain.FixedClock(new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero))), poll);
}

/// <summary>
/// 1063-B: the git CLI reader as the tick saw it before the model: every call would start a git process, so a test
/// of a read from the model asserts <see cref="Calls"/> stays 0 (MM-15).
/// </summary>
internal sealed class CountingMain : IMainRepository
{
    public List<string> Calls { get; } = [];

    public bool Fetch()
    {
        Calls.Add("fetch");
        return true;
    }

    public IReadOnlyList<TreeEntry>? ListTree(string folder)
    {
        Calls.Add("ls-tree " + folder);
        return [];
    }

    public string? ReadBlob(string blobId)
    {
        Calls.Add("cat-file " + blobId);
        return null;
    }

    public string? ReadFile(string path)
    {
        Calls.Add("show " + path);
        return null;
    }

    public string? HeadSha()
    {
        Calls.Add("rev-parse");
        return null;
    }
}
