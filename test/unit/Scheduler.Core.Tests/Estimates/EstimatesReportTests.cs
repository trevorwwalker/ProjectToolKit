// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json.Nodes;
using Scheduler.Core.Estimates;
using Xunit;
using static Scheduler.Core.Tests.Estimates.EstimateTestRows;

namespace Scheduler.Core.Tests.Estimates;

/// <summary>EST-31 and EST-33: the document of design section 3.</summary>
public sealed class EstimatesReportTests
{
    // The example of estimates.md section 3, as written there (the golden file).
    private const string Golden = """
{
  "schema": 1,
  "project": "UltraCommand",
  "written": "2026-11-24T21:30:05-08:00",
  "scheduler": "3.4.42",
  "units": { "time": "minutes", "share": "percent" },
  "window": { "days": 60, "newestRuns": 100, "minRunsMedian": 3, "minRunsP90": 10, "minRunsShare": 10 },
  "rows": { "project": 412, "pool": 1650, "skipped": 0 },
  "help": "Figures are history, not promises. Each figure says where it comes from (project = this project's own runs, pool = every project in the pool, none = too few runs anywhere) and how many runs it rests on. A group named category:<L> holds runs of cards that had no Kind: line.",
  "runTime": [
    { "worker": "Opus", "kind": "design",
      "median": { "minutes": 38, "source": "project", "runs": 14 },
      "p90": { "minutes": 71, "source": "project", "runs": 14 },
      "from": "2026-10-02T09:14:00-07:00", "to": "2026-11-24T20:33:09-08:00" },
    { "worker": "Sonnet", "kind": "implementation",
      "median": { "minutes": 22, "source": "project", "runs": 6 },
      "p90": { "minutes": 55, "source": "pool", "runs": 48 },
      "from": "2026-11-19T10:02:11-08:00", "to": "2026-11-24T18:28:47-08:00" },
    { "worker": "Spark", "kind": "check",
      "median": { "minutes": null, "source": "none", "runs": 1 },
      "p90": { "minutes": null, "source": "none", "runs": 1 },
      "from": "2026-11-23T14:40:00-08:00", "to": "2026-11-23T14:40:00-08:00" },
    { "worker": "Sonnet", "category": "C",
      "median": { "minutes": 6, "source": "project", "runs": 9 },
      "p90": { "minutes": null, "source": "none", "runs": 9 },
      "from": "2026-11-18T11:00:00-08:00", "to": "2026-11-24T20:28:01-08:00" }
  ],
  "review": [
    { "kind": "all",
      "median": { "minutes": 13, "source": "project", "runs": 100 },
      "p90": { "minutes": 307, "source": "project", "runs": 100 },
      "toDone": 91, "toQueue": 9 },
    { "kind": "design",
      "median": { "minutes": 95, "source": "pool", "runs": 12 },
      "p90": { "minutes": 840, "source": "pool", "runs": 12 },
      "toDone": 2, "toQueue": 0 }
  ],
  "reviewOpen": { "count": 1, "oldestMinutes": 42 },
  "outcomes": [
    { "worker": "Opus", "source": "project", "runs": 100,
      "done": { "count": 52, "share": 52 }, "review": { "count": 30, "share": 30 },
      "question": { "count": 9, "share": 9 }, "planner": { "count": 1, "share": 1 },
      "yielded": { "count": 3, "share": 3 }, "timeLimit": { "count": 2, "share": 2 },
      "stopped": { "count": 1, "share": 1 }, "failed": { "count": 2, "share": 2 },
      "outOfQuota": 0, "didNotStart": 4, "notCounted": 1,
      "cards": 80, "moreThanOneRun": 7, "runsPerCard": 1.1 },
    { "worker": "Spark", "source": "pool", "runs": 23,
      "done": { "count": 15, "share": 65 }, "review": { "count": 2, "share": 9 },
      "question": { "count": 1, "share": 4 }, "planner": { "count": 0, "share": 0 },
      "yielded": { "count": 0, "share": 0 }, "timeLimit": { "count": 3, "share": 13 },
      "stopped": { "count": 0, "share": 0 }, "failed": { "count": 2, "share": 9 },
      "outOfQuota": 0, "didNotStart": 0, "notCounted": 0,
      "cards": 17, "moreThanOneRun": 4, "runsPerCard": 1.3 }
  ]
}
""";

    private static readonly ReviewStaySource NoStays = new([], new Dictionary<string, string>());

    [Fact]
    public void EST_33_TheSection3Example_ParsesIntoTheReportType_AndBackUnchanged()
    {
        var report = EstimatesReport.FromJson(Golden);

        Assert.NotNull(report);
        Assert.Equal("UltraCommand", report.Project);
        Assert.Equal(4, report.RunTime.Count);
        Assert.Null(report.RunTime[2].Median.Minutes);
        Assert.Equal("C", report.RunTime[3].Category);
        Assert.Null(report.RunTime[3].Kind);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Golden), JsonNode.Parse(report.ToJson())));
    }

    [Fact]
    public void EST_33_ARunTimeEntry_HasAKindOrACategory_NeverBoth()
    {
        var rows = Rows(
            [.. Many(3, i => Line(kind: "design", minutesAgo: i, card: $"{i + 1}-A-x")),
             .. Many(3, i => Line(kind: "", lane: "C", minutesAgo: i, card: $"{i + 10}-C-x"))]);

        var json = JsonNode.Parse(EstimatesReport.ForProject("P", new EstimateSource(rows, 0, NoStays), null, [], Now, "1.2.3").ToJson())!;

        var entries = json["runTime"]!.AsArray();
        Assert.Equal(2, entries.Count);
        Assert.Equal("design", (string?)entries[0]!["kind"]);
        Assert.Null(entries[0]!["category"]);
        Assert.Equal("C", (string?)entries[1]!["category"]);
        Assert.Null(entries[1]!["kind"]);
        Assert.Equal(1, (int?)json["schema"]);
        Assert.Equal("1.2.3", (string?)json["scheduler"]);
        Assert.Equal("project", (string?)entries[0]!["median"]!["source"]);
    }

    [Fact]
    public void EST_27_AProjectAlone_NeverHasAPoolSource()
    {
        var rows = Rows([.. Many(2, i => Line(kind: "design", minutesAgo: i, card: $"{i + 1}-A-x"))]);

        var report = EstimatesReport.ForProject("P", new EstimateSource(rows, 0, NoStays), null, [], Now, "1");

        Assert.DoesNotContain("\"source\": \"pool\"", report.ToJson());
        Assert.Equal(0, report.Rows.Pool);
    }

    [Fact]
    public void EST_31_ThePoolReport_HasNoProject_AndEverySourceIsThePool()
    {
        var rows = Rows([.. Many(12, i => Line(kind: "design", minutesAgo: i, card: $"{i + 1}-A-x"))]);
        var other = Rows([.. Many(12, i => Line(kind: "design", minutesAgo: i, card: $"{i + 1}-A-x"))]);

        var report = EstimatesReport.ForPool([new EstimateSource(rows, 0, NoStays), new EstimateSource(other, 1, NoStays)], [], Now, "1");

        Assert.Null(report.Project);
        Assert.Equal(24, report.Rows.Pool);
        Assert.Equal(1, report.Rows.Skipped);
        Assert.All(report.RunTime, e => Assert.Equal("pool", e.Median.Source));
        Assert.All(report.Outcomes, o => Assert.Equal("pool", o.Source));
    }

    [Fact]
    public void EST_27_CardTwelveAOfTwoProjects_IsTwoCardsInThePoolReport()
    {
        var one = Rows(Line(card: "12-A-x"));
        var two = Rows(Line(card: "12-A-y"));

        var report = EstimatesReport.ForPool([new EstimateSource(one, 0, NoStays), new EstimateSource(two, 0, NoStays)], [], Now, "1");

        Assert.Equal(2, Assert.Single(report.Outcomes).Cards);
    }
}
