// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests.Fakes;

/// <summary>
/// Builds whose only difference is the number they report (967-B): the version is keyed by the name of the
/// folder the exe sits in, so a test says <c>scheduler</c> is one version and <c>scheduler.next</c> another
/// without any exe being built or run.
/// </summary>
public sealed class FakeVersionReader : IVersionReader
{
    /// <summary>The version an exe reports, keyed by the folder it is in ("scheduler", "scheduler.next").</summary>
    public Dictionary<string, string> ByFolder { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string Read(string exePath)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(exePath) ?? "");
        return ByFolder.TryGetValue(folder, out var version) ? version : "";
    }
}
