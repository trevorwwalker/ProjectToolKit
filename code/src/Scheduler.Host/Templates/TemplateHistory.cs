// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scheduler.Host.Templates;

/// <summary>
/// INS-17: the past versions of each template in a kit folder that has no <c>.git</c>, read from
/// <c>assets/templates/template-history.json</c> (written into the release from the kit's git history):
/// <c>{ "version": "&lt;kit version&gt;", "templates": { "&lt;template path&gt;": ["&lt;git blob id&gt;", ...] } }</c>.
/// </summary>
public sealed class TemplateHistory
{
    /// <summary>The manifest's path under the kit.</summary>
    public const string RelativePath = GenericFiles.TemplatesDir + "/template-history.json";

    private readonly Dictionary<string, HashSet<string>> _blobs;

    private TemplateHistory(string version, Dictionary<string, HashSet<string>> blobs)
    {
        Version = version;
        _blobs = blobs;
    }

    /// <summary>The kit version the manifest was written for: the trigger for a new check.</summary>
    public string Version { get; }

    /// <summary>The manifest of the kit at <paramref name="kitDir"/>, or null when it is missing or unreadable.</summary>
    public static TemplateHistory? Load(string kitDir)
    {
        try
        {
            var path = Path.Combine(kitDir, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(v.GetString())
                || !root.TryGetProperty("templates", out var t) || t.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var blobs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var p in t.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in p.Value.EnumerateArray())
                {
                    if (id.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    set.Add(id.GetString()!);
                }

                blobs[p.Name] = set;
            }

            return new TemplateHistory(v.GetString()!, blobs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="text"/> (as the project stores it, LF line ends) is a blob the template has had on the kit's main.</summary>
    public bool IsPastVersion(string template, string text) =>
        _blobs.TryGetValue(template, out var set) && set.Contains(BlobId(text));

    /// <summary>The id git gives a file with this text (<c>git hash-object</c>): SHA-1 of "blob &lt;length&gt;\0" and the normalized UTF-8 bytes.</summary>
    public static string BlobId(string text)
    {
        var body = Encoding.UTF8.GetBytes(TemplateState.Normalize(text));
        var head = Encoding.ASCII.GetBytes($"blob {body.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. head, .. body]));
    }
}
