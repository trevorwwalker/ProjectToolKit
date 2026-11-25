// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 1247-C (INS-13): a tray started with no pool file says so in one message box that names the guide's
/// create-a-project step, which is what writes the file, instead of the scheduler's error.
/// </summary>
public class TrayNoPoolFileTests
{
    private const string LocalAppData = @"C:\Users\you\AppData\Local";

    // INS-13: the default pool file is not there, so the tray says what to do next.
    [Fact]
    public void INS_13_PoolWithNoPoolFile_NamesTheCreateAProjectStep()
    {
        var message = TrayApp.NoPoolFileMessage(TrayArguments.Parse(["-Pool"]), LocalAppData, _ => false);

        Assert.NotNull(message);
        Assert.Contains(@"ProjectToolkit\pool.json", message, StringComparison.Ordinal);
        Assert.Contains("-Action new-project", message, StringComparison.Ordinal);
        Assert.Contains("getting-started.md", message, StringComparison.Ordinal);
        Assert.Contains("Create the project", message, StringComparison.Ordinal);
    }

    // MP-C1: the file it names is the one the start means, so a -Pool with its own file points at that.
    [Fact]
    public void INS_13_PoolWithANamedMissingFile_NamesThatFile()
    {
        var message = TrayApp.NoPoolFileMessage(TrayArguments.Parse(["-Pool", @"D:\p\mine.json"]), LocalAppData, _ => false);

        Assert.NotNull(message);
        Assert.Contains(@"D:\p\mine.json", message, StringComparison.Ordinal);
        Assert.DoesNotContain(@"ProjectToolkit\pool.json", message, StringComparison.Ordinal);
    }

    // INS-13 is only the first start: with the file there the tray starts as it did.
    [Fact]
    public void INS_13_PoolWithItsPoolFile_SaysNothing()
    {
        var args = TrayArguments.Parse(["-Pool"]);

        Assert.Null(TrayApp.NoPoolFileMessage(args, LocalAppData, _ => true));
        Assert.Null(TrayApp.NoPoolFileMessage(args, LocalAppData, path => path.EndsWith("pool.json", StringComparison.Ordinal)));
    }

    // MP-C9: a repository start needs no pool file, and is never stopped by one.
    [Fact]
    public void INS_13_ARepositoryStart_SaysNothing() =>
        Assert.Null(TrayApp.NoPoolFileMessage(TrayArguments.Parse([@"C:\Projects\Notes"]), LocalAppData, _ => false));

    // INS-13 is a clean stop, not an error: the tray gives one short message, no usage line and no blame.
    [Fact]
    public void INS_13_TheMessage_IsOneShortInstruction()
    {
        var message = TrayApp.NoPoolFileMessage(TrayArguments.Parse(["-Pool"]), LocalAppData, _ => false);

        Assert.NotNull(message);
        Assert.DoesNotContain(TrayArguments.Usage, message, StringComparison.Ordinal);
        Assert.DoesNotContain("error", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(message.Length < 700, $"the message box is {message.Length} characters");
    }
}
