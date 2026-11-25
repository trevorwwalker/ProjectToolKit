// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Moves;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 986-B: Open brings the opened card or log to the front (MOVE-9's Raise-Editor, L1575), on a fake
/// window API and a fake clock: the started process's window, or a running app's window found by
/// title, is restored and brought forward; a refused foreground flashes it; nothing looks longer than
/// the limit, and the page's action does not wait for the raise.
/// </summary>
public class WindowRaiserTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    // A new process's minimized window is restored and brought forward, and the process may take the foreground.
    [Fact]
    public void Raise_NewProcess_WindowRestoredAndBroughtForward()
    {
        var api = new FakeWindows { MainWindows = { [42] = 7 }, Minimized = { 7 } };

        var outcome = Raiser(api).Raise(@"C:\repo\jobs\queue\1-A-x.md", 42);

        Assert.Equal(RaiseOutcome.Front, outcome);
        Assert.Equal([42], api.Allowed);
        Assert.Equal([7], api.Restored);
        Assert.Equal([7], api.Fronted);
        Assert.Empty(api.Flashed);
    }

    // A window titled with the file name wins over the started process's own window (a hand-off process's).
    [Fact]
    public void Raise_TitledWindow_PreferredOverStartedProcess()
    {
        var api = new FakeWindows { MainWindows = { [42] = 7 }, Titled = { ["x.md - Visual Studio Code"] = 9 } };

        Raiser(api).Raise(@"C:\repo\x.md", 42);

        Assert.Equal([9], api.Fronted);
    }

    // A started process's window that is gone at the next look (a hand-off process) is never raised.
    [Fact]
    public void Raise_PassingWindow_NotRaised()
    {
        var api = new FakeWindows { MainWindows = { [42] = 7 } };
        var raiser = new WindowRaiser(api, Limit, Step, d =>
        {
            api.Clock += d;
            api.MainWindows.Remove(42);
        });

        Assert.Equal(RaiseOutcome.NotFound, raiser.Raise(@"C:\repo\x.md", 42));
        Assert.Empty(api.Fronted);
    }

    // A window that is not minimized is not restored (restoring would un-maximize it).
    [Fact]
    public void Raise_WindowNotMinimized_NotRestored()
    {
        var api = new FakeWindows { MainWindows = { [42] = 7 } };

        Raiser(api).Raise(@"C:\repo\x.md", 42);

        Assert.Empty(api.Restored);
        Assert.Equal([7], api.Fronted);
    }

    // An already-running app (no process started, or the started one has no window) is found by the file name in its title.
    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    public void Raise_RunningApp_WindowFoundByTitle(int? pid)
    {
        var api = new FakeWindows { Titled = { ["1-A-x.md"] = 9 } };

        var outcome = Raiser(api).Raise(@"C:\repo\jobs\queue\1-A-x.md", pid);

        Assert.Equal(RaiseOutcome.Front, outcome);
        Assert.Equal([9], api.Fronted);
    }

    // When Windows refuses the foreground, the window flashes in the taskbar instead.
    [Fact]
    public void Raise_FocusRefused_Flashes()
    {
        var api = new FakeWindows { MainWindows = { [42] = 7 }, Refuse = true };

        var outcome = Raiser(api).Raise(@"C:\repo\x.md", 42);

        Assert.Equal(RaiseOutcome.Flashed, outcome);
        Assert.Equal([7], api.Flashed);
    }

    // A window that shows up late is still found, after waiting.
    [Fact]
    public void Raise_WindowAppearsLate_FoundAfterWaiting()
    {
        var api = new FakeWindows();
        var raiser = new WindowRaiser(api, Limit, Step, d =>
        {
            api.Clock += d;
            if (api.Clock >= TimeSpan.FromMilliseconds(500))
            {
                api.MainWindows[42] = 7;
            }
        });

        Assert.Equal(RaiseOutcome.Front, raiser.Raise(@"C:\repo\x.md", 42));
        Assert.Equal(TimeSpan.FromMilliseconds(600), api.Clock);
    }

    // No window ever: it gives up, having waited no longer than the limit.
    [Fact]
    public void Raise_NoWindow_GivesUpWithinLimit()
    {
        var api = new FakeWindows();

        var outcome = Raiser(api).Raise(@"C:\repo\x.md", 42);

        Assert.Equal(RaiseOutcome.NotFound, outcome);
        Assert.True(api.Clock <= Limit, $"waited {api.Clock}");
        Assert.True(api.Clock >= Limit - Step, $"gave up early, after {api.Clock}");
        Assert.Empty(api.Fronted);
        Assert.Empty(api.Flashed);
    }

    // The launcher opens the file, answers at once, and raises the window on its background runner.
    [Fact]
    public void ShellLauncher_Open_StartsThenRaisesInBackground()
    {
        var api = new FakeWindows { StartPid = 42, MainWindows = { [42] = 7 } };
        var queued = new List<Action>();
        var launcher = new ShellLauncher(api, queued.Add);

        launcher.Open(@"C:\repo\x.md");

        Assert.Equal([@"C:\repo\x.md"], api.Started);
        Assert.Empty(api.Fronted);
        var raise = Assert.Single(queued);
        raise();
        Assert.Equal([7], api.Fronted);
    }

    // A file the shell cannot open still throws from Open, so the page shows its notice (MOVE-11).
    [Fact]
    public void ShellLauncher_StartFails_Throws()
    {
        var api = new FakeWindows { StartFails = true };
        var queued = new List<Action>();

        Assert.Throws<System.ComponentModel.Win32Exception>(() => new ShellLauncher(api, queued.Add).Open(@"C:\x.md"));
        Assert.Empty(queued);
    }

    private static WindowRaiser Raiser(FakeWindows api) => new(api, Limit, Step, d => api.Clock += d);

    private sealed class FakeWindows : IWindowApi
    {
        public Dictionary<int, nint> MainWindows { get; } = [];

        public Dictionary<string, nint> Titled { get; } = [];

        public HashSet<nint> Minimized { get; } = [];

        public bool Refuse { get; init; }

        public bool StartFails { get; init; }

        public int? StartPid { get; init; }

        public TimeSpan Clock { get; set; }

        public List<string> Started { get; } = [];

        public List<int> Allowed { get; } = [];

        public List<nint> Restored { get; } = [];

        public List<nint> Fronted { get; } = [];

        public List<nint> Flashed { get; } = [];

        public int? Start(string file)
        {
            if (StartFails)
            {
                throw new System.ComponentModel.Win32Exception(2);
            }

            Started.Add(file);
            return StartPid;
        }

        public void AllowForeground(int pid) => Allowed.Add(pid);

        public nint MainWindow(int pid) => MainWindows.GetValueOrDefault(pid);

        public nint FindTitled(string text) =>
            Titled.FirstOrDefault(t => t.Key.Contains(text, StringComparison.OrdinalIgnoreCase)).Value;

        public bool IsMinimized(nint window) => Minimized.Contains(window);

        public void Restore(nint window) => Restored.Add(window);

        public bool BringToFront(nint window)
        {
            if (Refuse)
            {
                return false;
            }

            Fronted.Add(window);
            return true;
        }

        public void Flash(nint window) => Flashed.Add(window);
    }
}
