using System.Globalization;
using System.Text;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tick;

/// <summary>
/// The scheduler's log (690-B). Each line, stamped with the time, goes to <paramref name="echo"/>
/// (the console, and so to the tray's View log and the page) and to two files:
/// <list type="bullet">
/// <item><c>scheduler.log</c> in <paramref name="logDir"/>, the state log folder, which
/// <c>-Compare</c> reads (390-B) - unchanged from before 690-B.</item>
/// <item><c>scheduler-YYYYMMDD.log</c> in <paramref name="rollingDir"/>, the repository's
/// <c>jobs/.logs</c>: a new file at each local midnight, so the day a run died on is the day whose
/// file is read. Each line is written and the file closed again, so a hang or a kill leaves
/// everything that was logged up to it on disk.</item>
/// </list>
/// Dated files older than <c>keepDays</c> days are deleted at start and at each day change; a file
/// whose name carries no date is left alone. A file that cannot be written (disk full, locked by
/// another process) is said once in the log and then left alone - the scheduler carries on without
/// its file log rather than stopping over it.
/// <para>
/// The undated file is capped (1117-B): when <c>scheduler.log</c> passes <c>flatMaxBytes</c> it is
/// rolled aside as <c>scheduler.1.log</c>, the older <c>.1</c> and <c>.2</c> shift up to <c>.2</c>
/// and <c>.3</c>, the file past the third is deleted, and the next line starts a new
/// <c>scheduler.log</c>. The size is looked at at start and then at most once a minute, never per
/// line. A roll also drops a copy that is itself many times over the limit (<c>OversizeFactor</c>),
/// which is how a file that grew without a cap - the pool's gigabyte <c>scheduler.log</c> of 1104-B -
/// gives its disk space back at the first roll instead of sitting in the queue until three more rolls
/// push it out. A roll that cannot happen (a file another process holds) is said once and tried again
/// at the next check; it deletes nothing meanwhile; the dated files and their retention are not
/// touched by any of this.
/// </para>
/// </summary>
public sealed class FileSchedulerLog : ISchedulerLog
{
    /// <summary>How many days of dated files are kept (690-B).</summary>
    public const int DefaultKeepDays = 7;

    /// <summary>The start of a dated file's name.</summary>
    public const string RollingPrefix = "scheduler-";

    /// <summary>What every log file ends with.</summary>
    public const string LogExtension = ".log";

    /// <summary>The name of the undated file in the state log folder.</summary>
    public const string FlatFileName = FlatFilePrefix + LogExtension;

    /// <summary>What the undated file's name starts with, and what its rolled-aside copies use too.</summary>
    public const string FlatFilePrefix = "scheduler";

    /// <summary>The size the undated file may reach before it is rolled aside (1117-B).</summary>
    public const long DefaultFlatMaxBytes = 50L * 1024 * 1024;

    /// <summary>How often the undated file's size is looked at after start (1117-B).</summary>
    public static readonly TimeSpan DefaultFlatCheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>How many rolled-aside copies of the undated file are kept (1117-B).</summary>
    public const int FlatKeepOld = 3;

    /// <summary>
    /// How many times over the limit a rolled-aside copy may be before a roll deletes it rather than
    /// keep its place in the queue (1117-B). A copy a roll just made is a little over the limit - one
    /// minute's logging more - so one this much bigger is a file the cap never saw, and keeping three
    /// of those is what filled the pool's disk (1104-B).
    /// </summary>
    public const int OversizeFactor = 4;

    /// <summary>What the dated files are matched by: <c>scheduler-YYYYMMDD.log</c>.</summary>
    public const string RollingGlob = RollingPrefix + "*" + LogExtension;

    /// <summary>The folder of 690-B's dated files, inside the jobs folder.</summary>
    public const string RollingFolderName = ".logs";

    private const string DateFormat = "yyyyMMdd";
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string _logDir;
    private readonly string _rollingDir;
    private readonly IClock _clock;
    private readonly Action<string>? _echo;
    private readonly int _keepDays;
    private readonly long _flatMaxBytes;
    private readonly long _flatOversizeBytes;
    private readonly TimeSpan _flatCheckInterval;
    private readonly Lock _gate = new();

    private DateOnly _day;
    private DateTimeOffset _nextFlatCheck;
    private (string File, string Error)? _pending;
    private bool _warned;

    /// <param name="logDir">The state log folder: holds the undated <c>scheduler.log</c>.</param>
    /// <param name="rollingDir">Where the dated files go: the repository's <c>jobs/.logs</c>.</param>
    /// <param name="clock">The clock the time stamps and the day boundary are taken from.</param>
    /// <param name="echo">Writes the line where a person can see it; null when nothing is watching.</param>
    /// <param name="keepDays">How many days of dated files to keep.</param>
    /// <param name="flatMaxBytes">How big the undated file may get before it is rolled aside (1117-B).</param>
    /// <param name="flatCheckInterval">How often its size is looked at after start; null for once a minute.</param>
    public FileSchedulerLog(
        string logDir,
        string rollingDir,
        IClock clock,
        Action<string>? echo = null,
        int keepDays = DefaultKeepDays,
        long flatMaxBytes = DefaultFlatMaxBytes,
        TimeSpan? flatCheckInterval = null)
    {
        _logDir = logDir;
        _rollingDir = rollingDir;
        _clock = clock;
        _echo = echo;
        _keepDays = keepDays > 0 ? keepDays : DefaultKeepDays;
        _flatMaxBytes = flatMaxBytes > 0 ? flatMaxBytes : DefaultFlatMaxBytes;
        _flatOversizeBytes = _flatMaxBytes * OversizeFactor;
        _flatCheckInterval = flatCheckInterval ?? DefaultFlatCheckInterval;

        _day = DateOnly.FromDateTime(_clock.Now.LocalDateTime);
        _pending = Prune(_day);

        // The cap is honoured from the first line: a file left over the limit by an earlier run is
        // rolled aside here, and the next check is a whole interval away.
        _nextFlatCheck = _clock.Now.Add(_flatCheckInterval);
        RollFlatOverLimit();
    }

    /// <summary>
    /// The folder the dated files go in (690-B): <c>&lt;repo&gt;/&lt;jobs&gt;/.logs</c>. It is git
    /// ignored, so a log never reaches main and the planner outbox - which lists the batch folders
    /// inside its own folder, and main's jobs folder as git has it - never sees it.
    /// </summary>
    public static string RollingFolder(SchedulerConfig config) =>
        Path.Combine(config.Repo, config.JobsDir.TrimEnd('/', '\\'), RollingFolderName);

    /// <inheritdoc />
    public void Write(string line)
    {
        var text = $"{_clock.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {line}";
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(_clock.Now.LocalDateTime);
            var failure = _pending;
            _pending = null;

            if (_clock.Now >= _nextFlatCheck)
            {
                _nextFlatCheck = _clock.Now.Add(_flatCheckInterval);
                RollFlatOverLimit();
            }

            failure ??= Append(_logDir, FlatFileName, text);
            if (today != _day)
            {
                _day = today;
                failure ??= Prune(today);
            }

            failure ??= Append(_rollingDir, RollingName(today), text);

            Echo(text);
            if (failure is not null && !_warned)
            {
                _warned = true;
                Echo($"scheduler log file {failure.Value.File} is not being written: {failure.Value.Error}. "
                    + "The scheduler carries on; later log write failures are not reported.");
            }
        }
    }

    /// <summary>The dated file for one day.</summary>
    internal static string RollingName(DateOnly day) =>
        $"{RollingPrefix}{day.ToString(DateFormat, CultureInfo.InvariantCulture)}{LogExtension}";

    /// <summary>The name of one rolled-aside copy of the undated file: <c>scheduler.1.log</c>, <c>.2</c>, <c>.3</c>.</summary>
    internal static string RotatedName(int index) => $"{FlatFilePrefix}.{index}{LogExtension}";

    /// <summary>
    /// Rolls the undated file aside when it has passed the size limit (1117-B): the copy past the
    /// third is deleted, <c>.2</c> becomes <c>.3</c> and <c>.1</c> becomes <c>.2</c>, the file itself
    /// becomes <c>.1</c>, and the next line starts a new one. Called at start and then at most once a
    /// minute. When a file will not move - another process has it open - the roll is said once in the
    /// log and tried again at the next check; the lines meanwhile keep going where they were going.
    /// When a roll does finish, any copy far bigger than the cap goes with it. The dated files take no
    /// part in this.
    /// </summary>
    private void RollFlatOverLimit()
    {
        var current = Path.Combine(_logDir, FlatFileName);
        long size;
        try
        {
            if (!File.Exists(current))
            {
                return;
            }

            size = new FileInfo(current).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A size that cannot be read this time is no reason for a line; the next check looks again.
            return;
        }

        if (size <= _flatMaxBytes)
        {
            return;
        }

        try
        {
            var oldest = Path.Combine(_logDir, RotatedName(FlatKeepOld));
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var index = FlatKeepOld - 1; index >= 1; index--)
            {
                var from = Path.Combine(_logDir, RotatedName(index));
                if (File.Exists(from))
                {
                    File.Move(from, Path.Combine(_logDir, RotatedName(index + 1)), overwrite: true);
                }
            }

            File.Move(current, Path.Combine(_logDir, RotatedName(1)), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var megabytes = _flatMaxBytes / (1024 * 1024);
            var stamp = _clock.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            Echo($"{stamp} scheduler log file {FlatFileName} is over {megabytes} MB and did not roll over: {e.Message}. "
                + "It is tried again at the next check.");
            return;
        }

        // The roll worked, so an old copy no cap could have made - the pool's gigabyte file, which has
        // just become scheduler.1.log - gives its space back now instead of after three more rolls.
        DeleteOversizedCopies();
    }

    /// <summary>
    /// Deletes the rolled-aside copies that are <c>OversizeFactor</c> times over the limit (1117-B).
    /// Only a roll calls this, and only one that finished, so a blocked roll never throws history
    /// away; the copy the roll just made is measured too. A copy another process holds is said once
    /// and left for the next roll.
    /// </summary>
    private void DeleteOversizedCopies()
    {
        var limitMegabytes = _flatOversizeBytes / (1024 * 1024);
        for (var index = 1; index <= FlatKeepOld; index++)
        {
            var name = RotatedName(index);
            var path = Path.Combine(_logDir, name);
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length <= _flatOversizeBytes)
                {
                    continue;
                }

                var sizeMegabytes = info.Length / (1024 * 1024);
                File.Delete(path);
                Say($"scheduler log file {name} was {sizeMegabytes} MB, over the {limitMegabytes} MB limit for an old copy, "
                    + "and was deleted with the roll.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Say($"scheduler log file {name} is over the {limitMegabytes} MB limit for an old copy and could not be "
                    + $"deleted: {e.Message}. It is tried again at the next roll.");
            }
        }
    }

    /// <summary>Writes one line where a person can see it, stamped like the log's own lines.</summary>
    private void Say(string message) =>
        Echo($"{_clock.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {message}");

    /// <summary>The day a dated file's name carries, or false for a name that carries none.</summary>
    internal static bool TryGetDay(string fileName, out DateOnly day)
    {
        day = default;
        if (!fileName.StartsWith(RollingPrefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(LogExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var date = fileName[RollingPrefix.Length..^LogExtension.Length];
        if (!DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        day = DateOnly.FromDateTime(parsed.Date);
        return true;
    }

    /// <summary>
    /// Deletes the dated files older than the <c>keepDays</c> days that end today. A name without a
    /// date in it is left alone rather than guessed at, and a file that will not delete is tried
    /// again at the next day change.
    /// </summary>
    private (string File, string Error)? Prune(DateOnly today)
    {
        (string File, string Error)? failure = null;
        try
        {
            if (!Directory.Exists(_rollingDir))
            {
                return null;
            }

            var oldest = today.AddDays(1 - _keepDays);
            foreach (var path in Directory.EnumerateFiles(_rollingDir, RollingGlob))
            {
                var name = Path.GetFileName(path);
                if (!TryGetDay(name, out var day) || day >= oldest)
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    failure ??= (name, e.Message);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure ??= (RollingGlob, e.Message);
        }

        return failure;
    }

    /// <summary>Appends one line and closes the file again, so nothing sits in a buffer.</summary>
    private (string File, string Error)? Append(string dir, string name, string text)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, name), text + Environment.NewLine, Utf8);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (name, e.Message);
        }
    }

    private void Echo(string text)
    {
        try
        {
            _echo?.Invoke(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A console that has gone away (the tray closed) is not a reason to stop the scheduler either.
        }
    }
}
