using System.Text.Json;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Slots;

/// <summary>
/// Which slots are enabled, kept on this PC in workers-state.json (WRK-3). A slot not mentioned is
/// enabled. When there is no usable workers-state.json, 1.6's log-folder workers.json is read.
/// Every change is saved at once.
/// </summary>
public sealed class WorkersEnabledState
{
    private readonly IStateFiles _files;
    private readonly SortedDictionary<int, bool> _enabled = new();

    /// <summary>Load the saved flags.</summary>
    public WorkersEnabledState(IStateFiles files)
    {
        _files = files;
        foreach (var file in new[] { StateFile.WorkersState, StateFile.LegacyWorkers })
        {
            Parse(_files.Read(file));
            if (_enabled.Count > 0)
            {
                break;
            }
        }
    }

    /// <summary>Whether slot <paramref name="slot"/> is enabled; true when never set.</summary>
    public bool IsEnabled(int slot) => !_enabled.TryGetValue(slot, out var value) || value;

    /// <summary>Set and save a slot's flag.</summary>
    public void SetEnabled(int slot, bool enabled)
    {
        _enabled[slot] = enabled;
        var map = _enabled.ToDictionary(p => p.Key.ToString(), p => p.Value);
        _files.Write(StateFile.WorkersState, JsonSerializer.Serialize(new { enabled = map }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            if (!root.TryGetProperty("enabled", out var source) && !root.TryGetProperty("workers", out source))
            {
                return;
            }
            if (source.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (var p in source.EnumerateObject())
            {
                var value = p.Value;
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("enabled", out var inner))
                {
                    value = inner;
                }
                if (int.TryParse(p.Name, out var slot) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _enabled[slot] = value.GetBoolean();
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
