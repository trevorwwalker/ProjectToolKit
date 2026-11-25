// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 1083-B: <c>check-push</c> against a real temporary repository: a push that changes the <c>&lt;Version&gt;</c>
/// line to a number not above the remote main's is refused with "bump again", a greater one is accepted, a push
/// that leaves the line alone is not checked, and a process that is not a scheduler worker (no card, locks off)
/// is never checked.
/// </summary>
public class CheckPushVersionTests
{
    private const string Props = "code/src/Directory.Build.props";
    private const string Categories = """
        {"lanes":{"B":{"name":"host","purpose":"p","owns":["code/src/"],"except":[]}},"shared":{"about":"x","addOnly":["code/src/Directory.Build.props"],"appendOnly":[]}}
        """;

    private static string PropsText(string version) => $"<Project>\n  <PropertyGroup>\n    <Version>{version}</Version>\n  </PropertyGroup>\n</Project>\n";

    // The remote's main holds 3.0.50; the Seed then makes a local commit (not pushed) that sets `pushed`.
    private static (GitFixture Fx, string Local, string Remote) Rig(string pushed)
    {
        var fx = new GitFixture();
        fx.Push("base", new Dictionary<string, string?> { [Props] = PropsText("3.0.50"), ["jobs/categories.json"] = Categories, ["code/src/a.txt"] = "a\n" });
        var remote = GitFixture.Git(fx.Seed, "rev-parse", "HEAD");
        if (pushed.Length > 0)
        {
            File.WriteAllText(Path.Combine(fx.Seed, Props), PropsText(pushed));
        }
        else
        {
            File.WriteAllText(Path.Combine(fx.Seed, "code/src/a.txt"), "b\n");
        }

        GitFixture.Git(fx.Seed, "commit", "-q", "-a", "-m", "work");
        return (fx, GitFixture.Git(fx.Seed, "rev-parse", "HEAD"), remote);
    }

    private static (int Code, string Output) Check(GitFixture fx, string local, string remote, string? card = "1083-B-x")
    {
        var output = new StringWriter();
        var input = new StringReader($"refs/heads/main {local} refs/heads/main {remote}\n");
        var code = CheckPushCommand.Run(["origin"], n => n == "PTK_CARD" ? card : null, input, output, fx.Seed);
        return (code, output.ToString());
    }

    [Theory]
    [InlineData("3.0.49")]
    [InlineData("2.9.99")]
    public void LowerVersion_IsRefused(string pushed)
    {
        var (fx, local, remote) = Rig(pushed);
        using (fx)
        {
            var (code, text) = Check(fx, local, remote);
            Assert.Equal(CheckPushCommand.Refused, code);
            Assert.Contains("bump again from main's version", text, StringComparison.Ordinal);
            Assert.Contains($"version {pushed} is not above main's 3.0.50", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GreaterVersion_IsAccepted()
    {
        var (fx, local, remote) = Rig("3.0.51");
        using (fx)
        {
            var (code, text) = Check(fx, local, remote);
            Assert.True(code == 0, text);
            Assert.DoesNotContain("bump again", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VersionLineLeftAlone_IsNotChecked()
    {
        var (fx, local, remote) = Rig("");
        using (fx)
        {
            // code/src/a.txt is a code file the card holds no lock on, so it is refused for that, never for the version.
            var (_, text) = Check(fx, local, remote);
            Assert.DoesNotContain("version", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoCard_LocksOff_IsNeverChecked()
    {
        var (fx, local, remote) = Rig("3.0.49");
        using (fx)
        {
            var (code, text) = Check(fx, local, remote, card: null);
            Assert.Equal(0, code);
            Assert.Equal("", text);
        }
    }
}
