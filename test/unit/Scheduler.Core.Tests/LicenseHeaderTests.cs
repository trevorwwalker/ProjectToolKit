// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Tests;

/// <summary>1173-C: every source file starts with the license header, so new files keep it.</summary>
public class LicenseHeaderTests
{
    private const string Line1 = "Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.";
    private const string Line2 = "Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "code", "src", "SchedulerNext.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static bool HasHeader(string path, string comment)
    {
        var text = File.ReadAllText(path); // strips a UTF-8 BOM
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        return text.StartsWith(comment + " " + Line1 + nl + comment + " " + Line2 + nl, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Sources(string root, string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(root, folder), pattern, SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    [Fact]
    public void Header_EveryCsFile_StartsWithLicense()
    {
        var root = RepoRoot();
        var missing = Sources(root, Path.Combine("code", "src"), "*.cs")
            .Concat(Sources(root, Path.Combine("test", "unit"), "*.cs"))
            .Where(p => !HasHeader(p, "//")).Select(p => Path.GetRelativePath(root, p)).ToList();
        Assert.True(missing.Count == 0, "Missing license header: " + string.Join(", ", missing.Take(10)));
    }

    private const string L1 = "# " + Line1;
    private const string L2 = "# " + Line2;

    /// <summary>1271-D: both licence lines, one directly after the other, either as the first two lines, or as the
    /// first two non-blank lines after the closing of a help block that starts on the first line.</summary>
    internal static bool HasPs1License(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length >= 2 && lines[0] == L1 && lines[1] == L2) return true;
        if (!lines[0].StartsWith("<#", StringComparison.Ordinal)) return false;
        var end = Array.FindIndex(lines, l => l.TrimEnd().EndsWith("#>", StringComparison.Ordinal));
        if (end < 0) return false;
        var after = lines.Skip(end + 1).Where(l => l.Trim().Length > 0).Take(2).ToList();
        return after.Count == 2 && after[0] == L1 && after[1] == L2;
    }

    [Fact]
    public void Header_EveryBinPs1_HasLicenseFirstOrRightAfterItsHelp()
    {
        var root = RepoRoot();
        var missing = Directory.EnumerateFiles(Path.Combine(root, "bin"), "*.ps1", SearchOption.AllDirectories)
            .Where(p => !HasPs1License(File.ReadAllText(p))).Select(p => Path.GetRelativePath(root, p)).ToList();
        Assert.True(missing.Count == 0, "Missing license header: " + string.Join(", ", missing.Take(10)));
    }

    private const string Help = "<#\n.SYNOPSIS\nx\n#>\n";

    [Fact]
    public void HasPs1License_LinesFirst_Passes() =>
        Assert.True(HasPs1License(L1 + "\r\n" + L2 + "\r\n" + Help + "param()\n"));

    [Fact]
    public void HasPs1License_LinesRightAfterHelp_Passes() =>
        Assert.True(HasPs1License("﻿" + Help + "\n" + L1 + "\n" + L2 + "\n\nparam()\n"));

    [Fact]
    public void HasPs1License_LinesMissing_Fails() =>
        Assert.False(HasPs1License(Help + "param()\n"));

    [Fact]
    public void HasPs1License_LinesFurtherDownAfterCode_Fails() =>
        Assert.False(HasPs1License(Help + "param()\n" + L1 + "\n" + L2 + "\n"));

    [Fact]
    public void HasPs1License_HelpNotOnFirstLine_Fails() =>
        Assert.False(HasPs1License("param()\n" + Help + L1 + "\n" + L2 + "\n"));
}
