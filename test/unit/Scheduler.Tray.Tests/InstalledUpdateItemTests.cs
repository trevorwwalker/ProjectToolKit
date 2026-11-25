// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Tray.Tests.Fakes;

namespace Scheduler.Tray.Tests;

/// <summary>Card 1246-C (INS-18): an installed copy has no Host project, so its update item checks for a release.</summary>
public class InstalledUpdateItemTests
{
    private const string Exe = @"C:\kit\out\windows\scheduler\scheduler-next.exe";

    [Fact]
    public void A_checkout_with_the_host_project_is_a_source_checkout()
    {
        var fs = new FakeSwapFileSystem().WithFile(@"C:\kit\code\src\Scheduler.Host\Scheduler.Host.csproj", "<Project/>");

        Assert.True(DeployLayout.ForSchedulerExe(Exe).IsSourceCheckout(fs));
    }

    [Fact]
    public void A_copy_without_the_host_project_is_installed()
    {
        var fs = new FakeSwapFileSystem().WithFile(Exe, "exe");

        Assert.False(DeployLayout.ForSchedulerExe(Exe).IsSourceCheckout(fs));
    }

    [Theory]
    [InlineData(true, "Check for update...")]
    [InlineData(false, "Update and restart")]
    public void The_item_text_follows_the_layout(bool installed, string text) =>
        Assert.Equal(text, DeployLayout.UpdateItemText(installed));

    [Fact]
    public void The_check_opens_the_latest_release_page() =>
        Assert.Equal("https://github.com/trevorwwalker/ProjectToolkit/releases/latest", DeployLayout.LatestReleaseUrl);
}
