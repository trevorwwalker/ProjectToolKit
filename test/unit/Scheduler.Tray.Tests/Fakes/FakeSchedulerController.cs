// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests.Fakes;

/// <summary>
/// The scheduler a deploy swaps, with no process behind it (967-B): it records the stops and starts and
/// says whether each came out, which is how a test drives the rollback of a build that will not start.
/// </summary>
public sealed class FakeSchedulerController : ISchedulerController
{
    /// <summary>Whether the scheduler ends when it is asked to.</summary>
    public bool Stops { get; set; } = true;

    /// <summary>Whether a start reports the start line in time. The number it is given is which start it is (1 first).</summary>
    public Func<int, bool>? StartsWithin { get; set; }

    /// <summary>The versions each start was told it replaces, in order.</summary>
    public List<string> Replaces { get; } = [];

    /// <summary>The start windows each start was given, in order.</summary>
    public List<TimeSpan> Windows { get; } = [];

    /// <summary>How many times it was stopped.</summary>
    public int StopCount { get; private set; }

    /// <summary>Runs when it is stopped, so a test can move files as the stop happens.</summary>
    public Action? OnStop { get; set; }

    /// <inheritdoc/>
    public Task<bool> StopAsync(CancellationToken cancel)
    {
        StopCount++;
        OnStop?.Invoke();
        return Task.FromResult(Stops);
    }

    /// <inheritdoc/>
    public Task<bool> StartAsync(string replaces, TimeSpan window, CancellationToken cancel)
    {
        Replaces.Add(replaces);
        Windows.Add(window);
        return Task.FromResult(StartsWithin?.Invoke(Replaces.Count) ?? true);
    }
}
