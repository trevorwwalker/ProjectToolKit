using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scheduler.Host.Templates;

/// <summary>What the scheduler knows of one generic file in one project (card 540-B).</summary>
public sealed class TemplateFileState
{
    /// <summary>The hash of the template version the project last took (written by the scheduler, or found equal), or "".</summary>
    public string Taken { get; set; } = "";

    /// <summary>The hash of the template version the developer chose to keep the project's own copy against, or "".</summary>
    public string Kept { get; set; } = "";
}

/// <summary>
/// <c>templates.json</c> beside the other state files (card 540-B): for each generic file's path in
/// the project, the template version it last took and the one it was kept against. An unreadable
/// file counts as empty, and one that cannot be written is left alone, as the other state files.
/// </summary>
public sealed class TemplateState
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>The file's name in the log folder.</summary>
    public const string FileName = "templates.json";

    /// <summary>Each generic file's state, by its path in the project.</summary>
    public Dictionary<string, TemplateFileState> Files { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The state of <paramref name="target"/>, made when missing.</summary>
    public TemplateFileState Of(string target)
    {
        if (!Files.TryGetValue(target, out var s))
        {
            s = new TemplateFileState();
            Files[target] = s;
        }

        return s;
    }

    /// <summary>Read <paramref name="path"/>; empty when it is missing or unreadable.</summary>
    public static TemplateState Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<TemplateState>(File.ReadAllText(path), Options);
                if (s is not null)
                {
                    s.Files = new Dictionary<string, TemplateFileState>(s.Files ?? [], StringComparer.Ordinal);
                    return s;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new TemplateState();
    }

    /// <summary>Write to <paramref name="path"/>, leaving it alone when it cannot be written.</summary>
    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A file's text as the project stores it: LF line ends (its .gitattributes says eol=lf, as <c>Put</c> writes it).</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>The hash a version is known by: SHA-256 of its normalized text.</summary>
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(text))));
}
