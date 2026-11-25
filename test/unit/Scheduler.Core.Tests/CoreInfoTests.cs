// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Reflection;
using System.Text.RegularExpressions;

namespace Scheduler.Core.Tests;

/// <summary>
/// CFG-18 and card 951-A: the version is written once, as <c>&lt;Version&gt;</c> in
/// code/src/Directory.Build.props, and read back out of the build - the page, the log and
/// <c>--version</c> all show this, and none of them writes the number.
/// test-plans/config.md.
/// </summary>
public class CoreInfoTests
{
    // CFG-18 · the shown version is Major.Minor.Rev: three groups of digits, nothing else.
    [Fact]
    public void CoreInfo_Version_IsMajorMinorRevDigits() =>
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), CoreInfo.Version);

    // 951-A · it is the built assembly's own version, not a number in the source: the informational
    // version with the source sha the SDK appends after a '+' taken off.
    [Fact]
    public void CoreInfo_Version_IsTheBuildsInformationalVersionWithoutTheSha()
    {
        var informational = typeof(CoreInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?? throw new InvalidOperationException("this build has no informational version");
        var plus = informational.InformationalVersion.IndexOf('+');
        var expected = (plus < 0 ? informational.InformationalVersion : informational.InformationalVersion[..plus]).Trim();

        Assert.Equal(expected, CoreInfo.Version);
        Assert.DoesNotContain("+", CoreInfo.Version, StringComparison.Ordinal);
    }

    // CFG-18 · and it is the same Major.Minor.Rev the assembly's own version carries, so a build
    // whose two version stamps disagree is caught here rather than on the page. A three-part
    // <Version> lands in the assembly's four-part version as Major.Minor.Build.Revision, so the
    // Rev the build carries is Build, not Revision (950-C).
    [Fact]
    public void CoreInfo_Version_IsTheAssemblysMajorMinorRevision()
    {
        var version = typeof(CoreInfo).Assembly.GetName().Version
            ?? throw new InvalidOperationException("this build has no assembly version");
        Assert.Equal($"{version.Major}.{version.Minor}.{version.Build}", CoreInfo.Version);
    }
}
