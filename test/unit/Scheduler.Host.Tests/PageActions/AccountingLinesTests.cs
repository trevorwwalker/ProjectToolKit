// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 1116-B (state-cache.md MM-15: a page refresh costs little): accounting.csv only grows as runs finish, so the
/// page's copy of its lines is read on from where the last read ended, not again from the start; an unchanged
/// file is not read at all, and a file rewritten under it is read whole.
/// </summary>
[ParallelMethods]
public sealed class AccountingLinesTests : IDisposable
{
    private const string Header = "date,card,result\n";
    private const string Row1 = "2026-11-22,1104-B,done\n";
    private const string Row2 = "2026-11-22,1107-B,done\n";
    private const string Row3 = "2026-11-22,1116-B,review\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ptk-1116b-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public AccountingLinesTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "accounting.csv");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // MM-15: an appended row is read from the old end (plus the last line, to see the file was not rewritten).
    [Fact]
    public void Read_AfterARowIsAppended_ReadsOnlyFromTheOldEnd()
    {
        File.WriteAllText(_file, Header + Row1 + Row2);
        var lines = new AccountingLines();
        Assert.Equal(["date,card,result", "2026-11-22,1104-B,done", "2026-11-22,1107-B,done"], lines.Read(_file));
        var first = lines.BytesRead;
        Assert.Equal(Bytes(Header + Row1 + Row2), first);

        File.AppendAllText(_file, Row3);
        Assert.Equal(["date,card,result", "2026-11-22,1104-B,done", "2026-11-22,1107-B,done", "2026-11-22,1116-B,review"], lines.Read(_file));
        Assert.Equal(Bytes(Row2 + Row3), lines.BytesRead - first);
    }

    // MM-15: a file that has not changed since the last read is not read again.
    [Fact]
    public void Read_Unchanged_ReadsNothingAndKeepsTheLines()
    {
        File.WriteAllText(_file, Header + Row1);
        var lines = new AccountingLines();
        var before = lines.Read(_file);
        var read = lines.BytesRead;

        Assert.Same(before, lines.Read(_file));
        Assert.Equal(read, lines.BytesRead);
    }

    // A file rewritten under the reader (shorter, or longer with other lines) is read whole again.
    [Fact]
    public void Read_FileRewritten_ReadsItWhole()
    {
        File.WriteAllText(_file, Header + Row1 + Row2);
        var lines = new AccountingLines();
        _ = lines.Read(_file);

        File.WriteAllText(_file, Header + Row3);
        Assert.Equal(["date,card,result", "2026-11-22,1116-B,review"], lines.Read(_file));

        File.WriteAllText(_file, "date,card,result,type\n" + Row1 + Row2 + Row3);
        Assert.Equal(["date,card,result,type", "2026-11-22,1104-B,done", "2026-11-22,1107-B,done", "2026-11-22,1116-B,review"], lines.Read(_file));
    }

    // A row half written when the page reads is shown as it stands and read again whole once it is finished.
    [Fact]
    public void Read_HalfWrittenRow_IsCompletedByTheNextRead()
    {
        File.WriteAllText(_file, Header + "2026-11-22,1116");
        var lines = new AccountingLines();
        Assert.Equal(["date,card,result", "2026-11-22,1116"], lines.Read(_file));

        File.AppendAllText(_file, "-B,review\n");
        Assert.Equal(["date,card,result", "2026-11-22,1116-B,review"], lines.Read(_file));
    }

    // No file is no lines, as File.ReadAllLines' FileNotFoundException was for the caller.
    [Fact]
    public void Read_NoFile_Throws()
    {
        var lines = new AccountingLines();
        _ = Assert.ThrowsAny<IOException>(() => lines.Read(_file));
    }

    private static long Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}
