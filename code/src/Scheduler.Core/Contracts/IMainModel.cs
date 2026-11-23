using Scheduler.Core.MainModel;

namespace Scheduler.Core.Contracts;

/// <summary>
/// The in-memory model of main (state-cache.md, MM-4 to MM-14): the read side every consumer of
/// <c>&lt;remote&gt;/&lt;branch&gt;</c> uses instead of a git process. One per repository, shared
/// by the projects in it; each member answers from the current snapshot. A consumer that needs one
/// commit throughout a step takes <see cref="Snapshot"/> once and reads that. Before the first
/// load every list is empty and every text null. <see cref="IMainRepository"/> stays for the CLI
/// reader and the parity tests (MM-16).
/// </summary>
public interface IMainModel
{
    /// <summary>The current snapshot, or null before the first load.</summary>
    MainSnapshot? Snapshot { get; }

    /// <summary>The commit the model is at, or null before the first load.</summary>
    string? Commit { get; }

    /// <summary>Whether the done and archive headers have been read (MM-5's phase 2).</summary>
    bool DoneLoaded { get; }

    /// <summary>The open cards of <paramref name="jobsDir"/> (queue, active, review), parsed as <c>CardFiles</c> does.</summary>
    IReadOnlyList<Card> Cards(string jobsDir);

    /// <summary>The done and archive card headers of <paramref name="jobsDir"/>; empty until <see cref="DoneLoaded"/>.</summary>
    IReadOnlyList<DoneCard> DoneCards(string jobsDir);

    /// <summary>The requests of <paramref name="jobsDir"/>, as <c>RequestFiles</c> lists them.</summary>
    IReadOnlyList<JobRequest> Requests(string jobsDir);

    /// <summary>
    /// The text of a settings file or of any other path, which is then watched (MM-6): kept and
    /// kept current from here on. Null when it is not on main (and still watched).
    /// </summary>
    string? Settings(string path);

    /// <summary>A file's text at the snapshot's commit, read on demand in process (MM-4); null when it is not on main.</summary>
    string? Text(string path);

    /// <summary>The files under <paramref name="folder"/> of the jobs folders, as <c>ls-tree -r</c> lists them.</summary>
    IReadOnlyList<TreeEntry> Entries(string folder);

    /// <summary>The paths that differ between commits <paramref name="from"/> and <paramref name="to"/>, or null when either cannot be read.</summary>
    IReadOnlyList<string>? Changed(string from, string to);

    /// <summary>The commits after <paramref name="since"/>, oldest first; null when it is not in the log (MM-12).</summary>
    IReadOnlyList<MainCommit>? Commits(string? since);
}
