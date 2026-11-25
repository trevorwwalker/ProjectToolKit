// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Categories;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;

namespace Scheduler.Host.Pool;

/// <summary>What was read from one project's main (MP-1).</summary>
public sealed record ProjectJobs(
    string Name,
    string Repo,
    string JobsDir,
    int Weight,
    string? SchedulerJson,
    string? Readme,
    string? CategoriesJson,
    IReadOnlyList<string> Cards);

/// <summary>
/// MP-1: cards, categories, README and scheduler.json are read per project, each from its own
/// repository's main after a fetch there, never from another project's. 1063-B: from the repository's model of
/// main when it is loaded (its gate's fetch, no git process: MM-9, MM-14), else through git as before.
/// </summary>
/// <param name="repoFor">The repository to read for (repo, remote, branch); <see cref="MainRepository"/> in production.</param>
/// <param name="models">The model of (repo, remote, branch) when one is loaded; <see cref="IMainFeed.Shared"/> unless a test gives one.</param>
public sealed class PoolProjectReader(
    Func<string, string, string, IMainRepository> repoFor,
    Func<string, string, string, IMainFeed?>? models = null)
{
    private static readonly string[] CardFolders = ["queue", "active", "review", "done"];

    /// <summary>Read every project of the config.</summary>
    public IReadOnlyList<ProjectJobs> Read(PoolConfig config) => [.. config.Projects.Select(Read)];

    private ProjectJobs Read(PoolProjectEntry p)
    {
        if ((models ?? IMainFeed.Shared)(p.Repo, p.Remote, p.Branch) is { Model: { Snapshot: not null } model } feed)
        {
            return FromModel(p, feed, model);
        }

        var repo = repoFor(p.Repo, p.Remote, p.Branch);
        repo.Fetch();
        var cards = new List<string>();
        foreach (var folder in CardFolders)
        {
            var tree = repo.ListTree(p.JobsDir + "/" + folder);
            cards.AddRange((tree ?? []).Select(t => t.Path).Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
        }

        return new ProjectJobs(
            p.Name,
            p.Repo,
            p.JobsDir,
            p.Weight,
            repo.ReadFile(p.JobsDir + "/scheduler.json"),
            repo.ReadFile(p.JobsDir + "/README.md"),
            CategoriesLocation.Read(p.JobsDir, repo.ReadFile),
            cards);
    }

    private static ProjectJobs FromModel(PoolProjectEntry p, IMainFeed feed, IMainModel model)
    {
        if (feed.Fetch(FetchAsk.Poll) != FetchAnswer.Fetched)
        {
            _ = feed.Tick();   // MM-8: a fetch that ran has read the ref already
        }

        var snapshot = model.Snapshot!;
        var cards = new List<string>();
        foreach (var folder in CardFolders)
        {
            // ls-tree -r of the folder, as the git read lists it
            cards.AddRange(snapshot.Entries(p.JobsDir + "/" + folder).Select(t => t.Path)
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
        }

        return new ProjectJobs(
            p.Name,
            p.Repo,
            p.JobsDir,
            p.Weight,
            model.Settings(p.JobsDir + "/scheduler.json"),
            model.Settings(p.JobsDir + "/README.md"),
            CategoriesLocation.Read(p.JobsDir, model.Settings),
            cards);
    }
}
