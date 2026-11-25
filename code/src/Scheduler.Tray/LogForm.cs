// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>
/// The scheduler's console lines, live: the window opens scrolled to the end, the newest lines in
/// view and the earlier ones above, and new lines are added at the bottom.
/// </summary>
public sealed class LogForm : Form
{
    private readonly LogBuffer _log;
    private readonly TextBox _text;
    private readonly System.Windows.Forms.Timer _timer;
    private long _seen;
    private int _shown;

    /// <summary>A log window over <paramref name="log"/>.</summary>
    public LogForm(LogBuffer log, string title)
    {
        _log = log;
        Text = title;
        Width = 1000;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            MaxLength = int.MaxValue,
            HideSelection = false,
        };
        Controls.Add(_text);
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Pump();
        Shown += (_, _) =>
        {
            Reload();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Stop();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Reload()
    {
        var (lines, total) = _log.Snapshot();
        _text.Text = lines.Length == 0 ? "" : string.Join(Environment.NewLine, lines) + Environment.NewLine;
        _seen = total;
        _shown = lines.Length;
        ScrollToEnd();
    }

    private void Pump()
    {
        var (lines, total) = _log.Since(_seen);
        if (lines is null || _shown + lines.Length > _log.Capacity + 1000)
        {
            Reload();
            return;
        }

        if (lines.Length == 0)
        {
            return;
        }

        _text.AppendText(string.Join(Environment.NewLine, lines) + Environment.NewLine);
        _seen = total;
        _shown += lines.Length;
    }

    private void ScrollToEnd()
    {
        _text.SelectionStart = _text.TextLength;
        _text.SelectionLength = 0;
        _text.ScrollToCaret();
    }
}
