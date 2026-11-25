// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>
/// The scheduler's console lines, the last <see cref="Capacity"/> of them, safe to fill from the
/// output threads while the log window reads it. <see cref="Total"/> counts every line ever added,
/// so a reader can ask for just the lines it has not shown yet.
/// </summary>
public sealed class LogBuffer(int capacity = LogBuffer.DefaultCapacity)
{
    /// <summary>The lines the log view keeps (550-B).</summary>
    public const int DefaultCapacity = 5000;

    private readonly Queue<string> _lines = new();
    private readonly Lock _lock = new();
    private long _total;
    private string _last = "";

    /// <summary>The most lines kept.</summary>
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>Every line ever added, kept or not.</summary>
    public long Total
    {
        get
        {
            lock (_lock)
            {
                return _total;
            }
        }
    }

    /// <summary>The last line added, or empty.</summary>
    public string Last
    {
        get
        {
            lock (_lock)
            {
                return _last;
            }
        }
    }

    /// <summary>Adds a line, dropping the oldest when full.</summary>
    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Enqueue(line);
            if (_lines.Count > Capacity)
            {
                _lines.Dequeue();
            }

            _total++;
            _last = line;
        }
    }

    /// <summary>Every line kept, oldest first, and the <see cref="Total"/> they reach.</summary>
    public (string[] Lines, long Total) Snapshot()
    {
        lock (_lock)
        {
            return ([.. _lines], _total);
        }
    }

    /// <summary>
    /// The lines added after <paramref name="seen"/> lines, and the new total; null lines when some of
    /// them have already been dropped, so the reader must start again from <see cref="Snapshot"/>.
    /// </summary>
    public (string[]? Lines, long Total) Since(long seen)
    {
        lock (_lock)
        {
            var fresh = _total - seen;
            if (fresh < 0 || fresh > _lines.Count)
            {
                return (null, _total);
            }

            return ([.. _lines.Skip(_lines.Count - (int)fresh)], _total);
        }
    }
}
