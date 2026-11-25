// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Views;
using Scheduler.Host.State;

namespace Scheduler.Host.Views;

/// <summary>
/// 1131-B (VW-22): every run in a project's accounting.csv as <see cref="ViewRun"/>, read from where the last read
/// stopped (the file's length) and only again from the start when the file got shorter. A missing file is no runs;
/// a line still being written (no newline yet) is left for the next read.
/// </summary>
public sealed class RunLog(string path)
{
    private readonly List<ViewRun> _runs = [];
    private string[] _header = [];
    private long _offset;

    /// <summary>The bytes read from the file over this reader's life (the old part is never read twice).</summary>
    public long BytesRead { get; private set; }

    /// <summary>The runs read so far, oldest first.</summary>
    public IReadOnlyList<ViewRun> Runs => _runs;

    /// <summary>The file's length and last write (what the views version follows), or "" when it is missing.</summary>
    public string Stamp()
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length + "@" + info.LastWriteTimeUtc.Ticks : "";
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>Read what was appended since last time; true when the runs changed.</summary>
    public bool Refresh()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return Reset();
            }

            var changed = false;
            if (info.Length < _offset)
            {
                changed = Reset();
            }

            if (info.Length == _offset)
            {
                return changed;
            }

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = file.Seek(_offset, SeekOrigin.Begin);
            var buffer = new byte[file.Length - _offset];
            var got = 0;
            while (got < buffer.Length)
            {
                var n = file.Read(buffer, got, buffer.Length - got);
                if (n == 0)
                {
                    break;
                }

                got += n;
            }

            BytesRead += got;
            var end = got == 0 ? -1 : Array.LastIndexOf(buffer, (byte)'\n', got - 1);
            if (end < 0)
            {
                return changed;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, end + 1);
            _offset += end + 1;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }

                if (_header.Length == 0)
                {
                    _header = AccountingCsv.Split(line);
                }
                else if (RunOf(AccountingCsv.Split(line)) is { } run)
                {
                    _runs.Add(run);
                    changed = true;
                }
            }

            return changed;
        }
        catch (IOException)
        {
            return false; // held open: the next refresh tries again
        }
    }

    private bool Reset()
    {
        var had = _runs.Count > 0;
        _runs.Clear();
        _header = [];
        _offset = 0;
        return had;
    }

    private ViewRun? RunOf(string[] fields)
    {
        string Field(string name)
        {
            var i = Array.FindIndex(_header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i < fields.Length ? fields[i] : "";
        }

        if (!DateTimeOffset.TryParse(Field("finished"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var finished))
        {
            return null;
        }

        var card = Field("card");
        var id = ViewsSource.CardIdOf(card);
        var minutes = int.TryParse(Field("minutes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 0;
        return new ViewRun
        {
            CardId = id.Length > 0 ? id : card,
            Start = finished - TimeSpan.FromMinutes(minutes),
            End = finished,
            Result = Field("result"),
            Model = Field("model"),
            WorkerType = Field("type"),
            Slot = Field("slot"),
        };
    }
}
