using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tests.Slots;

/// <summary>In-memory <see cref="IStateFiles"/>.</summary>
internal sealed class FakeStateFiles : IStateFiles
{
    public Dictionary<StateFile, string> Files { get; } = new();

    public string? Read(StateFile file) => Files.GetValueOrDefault(file);

    public void Write(StateFile file, string text) => Files[file] = text;
}
