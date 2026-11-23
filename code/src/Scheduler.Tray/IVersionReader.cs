namespace Scheduler.Tray;

/// <summary>
/// The version an exe on disk carries (967-B), read through an interface so a deploy can be tested with
/// fake builds whose only difference is the number they report.
/// </summary>
public interface IVersionReader
{
    /// <summary>The exe's <c>Major.Minor.Rev</c>, or "" when it has no version or is not there.</summary>
    string Read(string exePath);
}
