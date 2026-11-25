// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Locks;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1189-B, DL-26: a push <c>check-push</c> refuses leaves a line beside the lock table that the scheduler
/// reads back and counts; a push it accepts leaves none.
/// </summary>
public sealed class PushRefusalRecordTests : IDisposable
{
    private const string Props = "code/src/Directory.Build.props";
    private const string Categories = """
        {"lanes":{"B":{"name":"host","purpose":"p","owns":["code/src/"],"except":[]}},"shared":{"about":"x","addOnly":["code/src/Directory.Build.props"],"appendOnly":[]}}
        """;

    private readonly TempDir _log = new();

    public void Dispose() => _log.Dispose();

    private static string PropsText(string v) => $"<Project>\n  <PropertyGroup>\n    <Version>{v}</Version>\n  </PropertyGroup>\n</Project>\n";

    private (int Code, LockTableFile Table) Push(string pushed)
    {
        var table = new LockTableFile(_log.Path, "repo", null);
        table.Save(LockTable.Empty("repo"));
        using var fx = new GitFixture();
        fx.Push("base", new Dictionary<string, string?> { [Props] = PropsText("3.0.50"), ["jobs/categories.json"] = Categories });
        var remote = GitFixture.Git(fx.Seed, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(fx.Seed, Props), PropsText(pushed));
        GitFixture.Git(fx.Seed, "commit", "-q", "-a", "-m", "work");
        var local = GitFixture.Git(fx.Seed, "rev-parse", "HEAD");
        var input = new StringReader($"refs/heads/main {local} refs/heads/main {remote}\n");
        var code = CheckPushCommand.Run(
            ["origin"],
            n => n switch { "PTK_CARD" => "1189-B-x", "PTK_LOG_DIR" => _log.Path, _ => null },
            input,
            new StringWriter(),
            fx.Seed);
        return (code, table);
    }

    [Fact]
    public void A_refused_push_is_recorded_for_the_page()
    {
        var (code, table) = Push("3.0.49");
        Assert.Equal(CheckPushCommand.Refused, code);
        Assert.Single(table.ReadPushRefusals());
    }

    [Fact]
    public void An_accepted_push_leaves_no_record()
    {
        var (code, table) = Push("3.0.51");
        Assert.Equal(0, code);
        Assert.Empty(table.ReadPushRefusals());
    }
}
