using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>
/// When the page snapshot is built (PAGE-2): after every click or tick, and every 3 s while
/// "/" was asked for in the last 15 s; the remote read-only version only while another PC asked
/// in the last 30 s. Pure logic over <see cref="IClock"/>.
/// </summary>
public sealed class PageCadence(IClock clock)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IndexWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RemoteWindow = TimeSpan.FromSeconds(30);

    private bool _dirty;
    private DateTimeOffset? _indexAsked;
    private DateTimeOffset? _remoteAsked;
    private DateTimeOffset? _lastBuilt;

    /// <summary>A click or a tick happened: a snapshot is due at once.</summary>
    public void NoteClickOrTick() => _dirty = true;

    /// <summary>"/" was asked for.</summary>
    public void NoteIndexAsked() => _indexAsked = clock.Now;

    /// <summary>Another PC asked for the page.</summary>
    public void NoteRemoteAsked() => _remoteAsked = clock.Now;

    /// <summary>Whether a snapshot should be built now.</summary>
    public bool SnapshotDue()
    {
        if (_dirty)
        {
            return true;
        }
        var now = clock.Now;
        if (_indexAsked is not { } asked || now - asked > IndexWindow)
        {
            return false;
        }
        return _lastBuilt is not { } last || now - last >= Interval;
    }

    /// <summary>A snapshot was built.</summary>
    public void NoteBuilt()
    {
        _dirty = false;
        _lastBuilt = clock.Now;
    }

    /// <summary>Whether the remote read-only version should be built too.</summary>
    public bool RemoteVersionWanted() =>
        _remoteAsked is { } asked && clock.Now - asked <= RemoteWindow;
}
