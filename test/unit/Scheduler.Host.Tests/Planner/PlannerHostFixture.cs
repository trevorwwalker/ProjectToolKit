// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Core.Planner;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Planner;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Planner;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class PlannerClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 15, 4, 5, TimeSpan.Zero);
}

/// <summary>
/// A temporary bare remote with a seeded main, the developer's clone (fetchRepo, left with an
/// uncommitted change), a temporary outbox folder and the planner step over them. Never this repository.
/// </summary>
internal sealed class PlannerHostFixture : IDisposable
{
    public const string Jobs = "docs/jobs";
    public const string SeedCard = "# 010-A: x\n\nLane: A\nStatus: ready\n";

    private readonly TempDir _temp = new();

    public GitFixture Fx { get; } = new();
    public string Outbox { get; }
    public PlannerClock Clock { get; } = new();
    public ListLog Log { get; } = new();
    public Dictionary<string, int> Retries { get; } = [];
    public bool Push { get; set; } = true;
    public PlannerStep Step { get; }

    public PlannerHostFixture()
    {
        Outbox = Path.Combine(_temp.Path, "outbox");
        Directory.CreateDirectory(Outbox);
        Fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/queue/010-A-x.md"] = SeedCard });
        GitFixture.Git(Fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(Fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(Fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(Fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(Fx.Repo, "pull", "-q", "origin", "main");
        var git = new GitRunner();
        var committer = new AnswersCommitter(git, Fx.Repo, Path.Combine(_temp.Path, "slots", "answers"), "origin", "main");
        var settings = new PlannerSettings { FetchRepo = Fx.Repo, Outbox = Outbox };
        Step = new PlannerStep(settings, git, committer, Clock, Log, () => Push, Retries, Jobs, "origin", "main");
    }

    /// <summary>Makes the remote refuse the first push it gets, as another clone pushing first would.</summary>
    public void RejectFirstPush()
    {
        var hook = Path.Combine(Fx.Bare, "hooks", "pre-receive");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        File.WriteAllText(hook, "#!/bin/sh\nif [ ! -e ./rejected-once ]; then : > ./rejected-once; echo 'rejected once' >&2; exit 1; fi\nexit 0\n");
    }

    /// <summary>Makes the remote refuse every push.</summary>
    public void RejectEveryPush()
    {
        var hook = Path.Combine(Fx.Bare, "hooks", "pre-receive");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        File.WriteAllText(hook, "#!/bin/sh\necho no >&2\nexit 1\n");
    }

    /// <summary>A batch folder in the outbox: batch.json from the message and ops, and the files it names.</summary>
    public string Batch(string name, string message, object[] ops, IDictionary<string, string>? files = null)
    {
        var dir = Path.Combine(Outbox, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "batch.json"),
            JsonSerializer.Serialize(new { message, trailers = new[] { "Co-Authored-By: P <p@x>" }, ops }));
        foreach (var (f, text) in files ?? new Dictionary<string, string>())
        {
            File.WriteAllText(Path.Combine(dir, f), text);
        }

        return dir;
    }

    public string RemoteSha() => GitFixture.Git(Fx.Bare, "rev-parse", "main");

    public void Dispose()
    {
        Fx.Dispose();
        _temp.Dispose();
    }
}
