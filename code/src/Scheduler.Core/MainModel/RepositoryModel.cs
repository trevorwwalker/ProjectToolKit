// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.MainModel;

/// <summary>
/// One repository's model of main (state-cache.md section 1): the current
/// <see cref="MainSnapshot"/>, replaced whole by one step at a time - the load (MM-5), the advance
/// when the tracking ref moved (MM-7, MM-8), the writer's apply (MM-11) or a new watched path
/// (MM-6). Readers take the snapshot with one reference read and no lock. A step that fails keeps
/// the previous snapshot and is counted (CARD-3, section 4); the Host decides what to do after
/// repeated failures. Pure but for the object source and the clock.
/// </summary>
public sealed class RepositoryModel(IObjectSource source, MainLayout layout, IClock clock) : IMainModel
{
    private readonly Lock gate = new();
    private volatile MainSnapshot? current;

    /// <inheritdoc/>
    public MainSnapshot? Snapshot => current;

    /// <inheritdoc/>
    public string? Commit => current?.Commit;

    /// <inheritdoc/>
    public bool DoneLoaded => current?.DoneLoaded ?? false;

    /// <summary>Steps that failed in a row (reset by one that works).</summary>
    public int Failures { get; private set; }

    /// <summary>Why the last failed step failed, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Phase 1 of MM-5 at <paramref name="commit"/>; false (and nothing changed) when it failed.</summary>
    public bool Load(string commit) => Step(_ => MainSnapshotBuilder.Load(source, layout, commit, current?.Watched));

    /// <summary>
    /// Phase 2 of MM-5: the done and archive headers. They are read without holding the step lock,
    /// so an advance in the meantime is not held up; if main moved meanwhile, the headers read are
    /// reused by blob id on the newer snapshot.
    /// </summary>
    public bool LoadDone()
    {
        if (current is not { } at)
        {
            return false;
        }

        MainSnapshot read;
        try
        {
            read = MainSnapshotBuilder.LoadDone(source, at);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            lock (gate)
            {
                Failed(e);
            }

            return false;
        }

        var known = new Dictionary<string, DoneCard>(StringComparer.Ordinal);
        foreach (var done in layout.JobsDirs.SelectMany(read.DoneCards))
        {
            known.TryAdd(done.BlobId, done);
        }

        return Step(s => s == at ? read : MainSnapshotBuilder.LoadDone(source, s!, known));
    }

    /// <summary>
    /// MM-8 and MM-10: the tracking ref as just read from the refs files. Equal to the snapshot's
    /// commit (or unknown): nothing is read. Different: the advance runs. True when the model moved.
    /// </summary>
    public bool RefRead(string? sha)
    {
        var at = current;
        if (sha is null || at is null || sha == at.Commit)
        {
            return false;
        }

        return Step(s => MainSnapshotBuilder.Advance(source, s!, sha, clock.Now));
    }

    /// <summary>MM-11: what the writer pushed. A push that did not succeed leaves the model as it was.</summary>
    public bool Pushed(PushedCommit push)
    {
        if (!push.Pushed || current is null)
        {
            return false;
        }

        return Step(s => MainSnapshotBuilder.Apply(source, s!, push, clock.Now));
    }

    /// <inheritdoc/>
    public IReadOnlyList<Card> Cards(string jobsDir) => current?.Cards(jobsDir) ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<DoneCard> DoneCards(string jobsDir) => current?.DoneCards(jobsDir) ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<JobRequest> Requests(string jobsDir) => current?.Requests(jobsDir) ?? [];

    /// <inheritdoc/>
    public string? Settings(string path)
    {
        var at = current;
        if (at is null)
        {
            return null;
        }

        path = path.Trim('/');
        if (at.Watched.Contains(path) || at.Layout.SettingsPaths.Contains(path))
        {
            return at.Settings(path);
        }

        Step(s => MainSnapshotBuilder.Watch(source, s!, path));
        return current?.Settings(path);
    }

    /// <inheritdoc/>
    public string? Text(string path)
    {
        var at = current;
        if (at is null)
        {
            return null;
        }

        path = path.Trim('/');
        if (at.Settings(path) is { } kept)
        {
            return kept;
        }

        try
        {
            var blob = at.Files.TryGetValue(path, out var known)
                ? known
                : at.Layout.Filter(at.Watched).Keep(path) ? null : TreeDiff.Lookup(source, at.Tree, path);
            return blob is null ? null : source.ReadBlob(blob);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<TreeEntry> Entries(string folder) => current?.Entries(folder) ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<string>? Changed(string from, string to)
    {
        try
        {
            var a = source.ReadCommit(from);
            var b = source.ReadCommit(to);
            return a is null || b is null
                ? null
                : [.. TreeDiff.Diff(source, a.Tree, b.Tree, PathFilter.All).Select(c => c.Path)];
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<MainCommit>? Commits(string? since) => current is { } at ? at.Commits(since) : null;

    // One step at a time per repository (MM-7); the new snapshot replaces the old in one assignment.
    private bool Step(Func<MainSnapshot?, MainSnapshot> make)
    {
        lock (gate)
        {
            try
            {
                var before = current;
                var next = make(before);
                current = next;
                Failures = 0;
                LastError = null;
                return !ReferenceEquals(before, next);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Failed(e);
                return false;
            }
        }
    }

    // Called with the gate held.
    private void Failed(Exception e)
    {
        Failures++;
        LastError = e.Message;
    }
}
