// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.MainModel;

/// <summary>
/// What one repository's model holds (state-cache.md section 1): the jobs folder of every project
/// in the repository (projects sharing a repository share its model, DL-22) and, under each, the
/// settings files whose text is kept (MM-6).
/// </summary>
public sealed class MainLayout
{
    /// <summary>The settings files kept as text under each jobs folder.</summary>
    public static IReadOnlyList<string> SettingsNames { get; } =
        ["scheduler.json", "categories.json", "workers.json", "models.json", "README.md"];

    /// <summary>A layout for the jobs folders <paramref name="jobsDirs"/> (repository paths, e.g. <c>jobs</c>).</summary>
    public MainLayout(IEnumerable<string> jobsDirs)
    {
        JobsDirs = [.. jobsDirs.Select(d => d.Trim('/')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        SettingsPaths = new HashSet<string>(
            JobsDirs.SelectMany(d => SettingsNames.Select(n => d + "/" + n)), StringComparer.Ordinal);
    }

    /// <summary>The jobs folders, in ordinal order.</summary>
    public IReadOnlyList<string> JobsDirs { get; }

    /// <summary>Every settings file's path.</summary>
    public IReadOnlySet<string> SettingsPaths { get; }

    /// <summary>The jobs folder <paramref name="path"/> is under, or null.</summary>
    public string? JobsDirOf(string path) =>
        JobsDirs.Where(d => path.StartsWith(d + "/", StringComparison.Ordinal)).MaxBy(d => d.Length);

    /// <summary>The filter for the jobs folders plus <paramref name="watched"/> files (MM-6, MM-7).</summary>
    public PathFilter Filter(IEnumerable<string> watched) => new(JobsDirs, watched);
}

/// <summary>One commit of the model's log (MM-12): what the audits and the review panel read.</summary>
/// <param name="Sha">The commit.</param>
/// <param name="Parent">Its first parent, or null for a root commit.</param>
/// <param name="Time">The committer time.</param>
/// <param name="Subject">The message's first line.</param>
/// <param name="Changes">Every path it changed against its first parent, with old and new blob ids.</param>
public sealed record MainCommit(
    string Sha,
    string? Parent,
    DateTimeOffset Time,
    string Subject,
    IReadOnlyList<PathChange> Changes);

/// <summary>
/// What the scheduler's writer pushed (MM-11): the new commit, its parent, and every path it wrote
/// or removed. <see cref="Pushed"/> false is a rejected or failed push, which leaves the model as it was.
/// </summary>
/// <param name="Commit">The pushed commit C.</param>
/// <param name="Parent">Its parent P.</param>
/// <param name="Paths">The paths written or removed.</param>
/// <param name="Pushed">Whether the push succeeded.</param>
public sealed record PushedCommit(string Commit, string Parent, IReadOnlyList<string> Paths, bool Pushed = true);
