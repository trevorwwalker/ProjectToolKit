using System.Text;

namespace Scheduler.Host.Observe;

/// <summary>
/// <c>scheduler-next -Compare &lt;old log&gt; &lt;new log&gt;</c> (roadmap item 1): prints the
/// <see cref="ComparisonReport"/>. Exit 0 when every tick agrees, 1 when some disagree, 2 when a log
/// cannot be read. The logs are read while their schedulers may still be writing them.
/// </summary>
public static class CompareCommand
{
    /// <summary>Compare the two logs and print the report on <paramref name="output"/>.</summary>
    public static int Run(string oldLog, string newLog, TextWriter output, TextWriter error)
    {
        var logs = new List<IReadOnlyList<string>>();
        foreach (var path in new[] { oldLog, newLog })
        {
            try
            {
                logs.Add(ReadShared(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error.WriteLine($"cannot read {path}: {e.Message}");
                return 2;
            }
        }

        var report = new LogComparison(LogComparison.DefaultTolerance).Compare(logs[0], logs[1]);
        foreach (var line in report.Lines())
        {
            output.WriteLine(line);
        }

        return report.Disagreements.Count == 0 ? 0 : 1;
    }

    private static List<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
