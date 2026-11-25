// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;

namespace Scheduler.Web.Tests.Server.Fakes;

/// <summary>Everything the scheduler would hand the page, held in memory for the tests.</summary>
internal sealed class FakePageData : IPageData
{
    /// <summary>The snapshot the page shows.</summary>
    public PageSnapshot Latest { get; set; } = new()
    {
        Built = new DateTimeOffset(2026, 11, 18, 11, 59, 0, TimeSpan.Zero),
        Project = "ProjectToolkit",
        Version = CoreInfo.Version,
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
    };

    /// <summary>The font file /font serves, or null when none is configured.</summary>
    public string? FontPath { get; set; }

    /// <summary>The log folder /log reads from.</summary>
    public string LogDirectory { get; set; } = "";

    /// <summary>Card text by file name, for /card.</summary>
    public Dictionary<string, string> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Request text by file name, for /request.</summary>
    public Dictionary<string, string> Requests { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The health log text, or null when no health check has run.</summary>
    public string? Health { get; set; }

    /// <summary>The text /status.json answers with.</summary>
    public string Status { get; set; } = """{"state":"running","slots":[]}""";

    /// <summary>The running workers the live view shows.</summary>
    public List<LiveWorker> Workers { get; } = [];

    /// <summary>Run before the workers are read; lets a test make a live build cost time.</summary>
    public Action? BeforeWorkers { get; set; }

    public PageSnapshot Snapshot() => Latest;

    public string? Card(string name) => Cards.GetValueOrDefault(name);

    public string? Request(string name) => Requests.GetValueOrDefault(name);

    public string? HealthText() => Health;

    public string StatusJson() => Status;

    /// <summary>Review panels by card name, for /review.</summary>
    public Dictionary<string, ReviewPanel> Reviews { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Repository files by name, for /repo-file.</summary>
    public Dictionary<string, string> RepoFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ReviewPanel? Review(string card) => Reviews.GetValueOrDefault(card);

    public string? RepoFile(string path) => RepoFiles.GetValueOrDefault(path);

    /// <summary>Plan documents by path, for /plan.</summary>
    public Dictionary<string, string> Plans { get; } = new(StringComparer.Ordinal);

    public string? PlanText(string path) => Plans.GetValueOrDefault(path);

    public IReadOnlyList<LiveWorker> RunningWorkers()
    {
        BeforeWorkers?.Invoke();
        return Workers;
    }
}
