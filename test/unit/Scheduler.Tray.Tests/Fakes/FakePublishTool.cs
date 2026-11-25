// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests.Fakes;

/// <summary>
/// A publish that publishes nothing (967-B): it says whether it worked and writes the files a publish would
/// have left into the fake file system, so a deploy can be tested with no <c>dotnet</c> and no build.
/// </summary>
public sealed class FakePublishTool(FakeSwapFileSystem fs) : IPublishTool
{
    /// <summary>Whether the publish is to finish successfully.</summary>
    public bool Ok { get; set; } = true;

    /// <summary>The file names a publish leaves in the output folder.</summary>
    public string[] Publishes { get; set; } = ["scheduler-next.exe", "Scheduler.Host.dll"];

    /// <summary>What the published files hold, so a test can tell one build from another.</summary>
    public string Content { get; set; } = "published";

    /// <summary>How many times it ran.</summary>
    public int Runs { get; private set; }

    /// <summary>The project of the last run.</summary>
    public string? Project { get; private set; }

    /// <summary>The output folder of the last run.</summary>
    public string? OutputDir { get; private set; }

    /// <inheritdoc/>
    public Task<bool> PublishAsync(string project, string outputDir, CancellationToken cancel)
    {
        Runs++;
        Project = project;
        OutputDir = outputDir;
        if (!Ok)
        {
            return Task.FromResult(false);
        }

        foreach (var name in Publishes)
        {
            fs.WithFile(Path.Combine(outputDir, name), $"{Content} {name}");
        }

        return Task.FromResult(true);
    }
}
