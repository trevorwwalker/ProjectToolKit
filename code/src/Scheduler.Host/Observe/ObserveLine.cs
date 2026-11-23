using System.Globalization;

namespace Scheduler.Host.Observe;

/// <summary>
/// The observe run's decision lines (roadmap item 1): <c>OBSERVE yyyy-MM-dd HH:mm:ss text</c>. The
/// scheduler log only stamps the time of day, and the old scheduler's log stamps the date too, so
/// each decision carries its own full time for <see cref="LogComparison"/>.
/// </summary>
public static class ObserveLine
{
    /// <summary>What every decision line starts with.</summary>
    public const string Mark = "OBSERVE ";

    /// <summary>The time format, the same as the old scheduler's log.</summary>
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>One decision line for <paramref name="text"/> at <paramref name="time"/>.</summary>
    public static string Format(DateTimeOffset time, string text) =>
        $"{Mark}{time.ToString(TimeFormat, CultureInfo.InvariantCulture)} {text}";

    /// <summary>The time and text of a decision line found anywhere in <paramref name="line"/>.</summary>
    public static bool TryParse(string line, out DateTime time, out string text)
    {
        time = default;
        text = "";
        var at = line.IndexOf(Mark, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var rest = line[(at + Mark.Length)..];
        if (rest.Length < TimeFormat.Length + 1
            || !DateTime.TryParseExact(rest[..TimeFormat.Length], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return false;
        }

        text = rest[(TimeFormat.Length + 1)..];
        return true;
    }
}
