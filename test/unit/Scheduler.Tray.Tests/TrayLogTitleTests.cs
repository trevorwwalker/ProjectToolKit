// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests;

public class TrayLogTitleTests
{
    // 1145-C: the log window is "Switchyard - log" whatever the tray is driving - pool mode and
    // single-project mode alike - so the title is fixed text, not the project's name.
    [Fact]
    public void The_log_window_is_titled_Switchyard_log()
    {
        using var form = new LogForm(new LogBuffer(), TrayApp.LogTitle);

        Assert.Equal("Switchyard - log", form.Text);
    }

    [Fact]
    public void The_title_carries_no_project_name_and_no_scheduler_word()
    {
        Assert.DoesNotContain("scheduler", TrayApp.LogTitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pool", TrayApp.LogTitle, StringComparison.OrdinalIgnoreCase);
    }
}
