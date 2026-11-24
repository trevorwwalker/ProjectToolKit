using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scheduler.Core.Launch;

/// <summary>
/// OpenCode's permissions live in its config, which a run reads from <c>OPENCODE_CONFIG_CONTENT</c>
/// (agents.md section 3), so the no-web rule (AG-11, WRK-11) is applied to that JSON and not to the
/// command line.
/// </summary>
public static class OpenCodePermissions
{
    /// <summary>The download commands denied when the worker has no web (WRK-11); a deny pattern is the command and <c> *</c>.</summary>
    public static IReadOnlyList<string> DownloadCommands { get; } =
        ["curl", "curl.exe", "wget", "Invoke-WebRequest", "iwr", "Invoke-RestMethod", "irm", "Start-BitsTransfer"];

    /// <summary>
    /// The config with the web tools and the download commands denied: <c>permission.webfetch</c> and
    /// <c>websearch</c> are <c>deny</c>, and <c>permission.bash</c> becomes a map whose own rule (what a
    /// plain string said, else <c>allow</c>) is <c>*</c> and which denies each download command. Whatever
    /// else the config holds stays. A missing or unreadable config starts from an empty one.
    /// </summary>
    public static string WithoutWeb(string? configJson)
    {
        var root = Parse(configJson);
        var permission = root["permission"] as JsonObject ?? [];
        root["permission"] = permission;

        permission["webfetch"] = "deny";
        permission["websearch"] = "deny";

        var bash = new JsonObject();
        switch (permission["bash"])
        {
            case JsonObject map:
                foreach (var pair in map)
                {
                    bash[pair.Key] = pair.Value?.DeepClone();
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var rule):
                bash["*"] = rule;
                break;
        }

        if (!bash.ContainsKey("*"))
        {
            bash["*"] = "allow";
        }

        foreach (var command in DownloadCommands)
        {
            bash[command + " *"] = "deny";
        }

        permission["bash"] = bash;
        return root.ToJsonString();
    }

    private static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
