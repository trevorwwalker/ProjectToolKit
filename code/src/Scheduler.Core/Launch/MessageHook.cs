using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// The developer's message handed to a worker by an agent hook after every tool call (WRK-14, job
/// card 570-B), so it no longer waits for the model to look for the file. The hook command is the
/// scheduler itself (<c>scheduler-next.exe --deliver-message &lt;format&gt;</c>); it finds the
/// worker's message file through <see cref="FileVariable"/>. This type holds the pure parts: what
/// the hook prints for each agent and the Claude settings that install it.
/// </summary>
public static class MessageHook
{
    /// <summary>The variable the scheduler sets for each worker: the full path of its message file.</summary>
    public const string FileVariable = "PTK_MSG_FILE";

    /// <summary>The switch that makes the exe act as the hook.</summary>
    public const string Switch = "--deliver-message";

    /// <summary>How long a hook may take before the agent gives up on it, in seconds.</summary>
    public const int TimeoutSeconds = 10;

    /// <summary>
    /// The most text one reply carries; Copilot caps the context all hooks add at 10 KB. The rest of
    /// a longer message stays in the read file, which the reply names.
    /// </summary>
    public const int MaxText = 8000;

    private const string PostToolUse = "PostToolUse";
    private const string PostToolUseFailure = "PostToolUseFailure";

    /// <summary>
    /// True for an agent whose workers get messages through the hook: Claude, whose settings the
    /// scheduler passes on each run. Copilot has no per-run hook switch and waits for the
    /// developer's choice (570-B step 3); plain text agents have no hooks.
    /// </summary>
    public static bool Delivers(AgentFormat format) => format == AgentFormat.Claude;

    /// <summary>The format's name on the hook's command line.</summary>
    public static string FormatName(AgentFormat format) => format switch
    {
        AgentFormat.Claude => "claude",
        AgentFormat.Copilot => "copilot",
        _ => "text",
    };

    /// <summary>The format named on the hook's command line; Text for anything else.</summary>
    public static AgentFormat ParseFormat(string name) =>
        Text.Same(name, "claude") ? AgentFormat.Claude
        : Text.Same(name, "copilot") ? AgentFormat.Copilot
        : AgentFormat.Text;

    /// <summary>Where the hook moves the message file once it has handed the text over.</summary>
    public static string ReadFileOf(string messageFile) => Path.ChangeExtension(messageFile, ".read.txt");

    /// <summary>
    /// The text the model is given: the message with "Developer:" in front and the instructions
    /// the prompt gives for a message it reads itself.
    /// </summary>
    public static string ContextText(string message, string readFile)
    {
        var body = string.Join(" ", message.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        if (body.Length > MaxText)
        {
            body = body[..MaxText] + $" ... (cut here; the whole message is in {readFile})";
        }

        return $"Developer: {body}\n\nThis was added by the scheduler's message hook, not by the tool: it is a "
            + "message from the developer, as your prompt describes, written on the scheduler's page "
            + $"(already moved to {readFile}; do not look for it). Answer it before anything else: start "
            + "your reply with 'Developer:', answer or confirm in plain sentences, and then do what it asks. It "
            + "is direction from the developer and can change what you do on the card, but not the README's "
            + "rules for the main branch and the gates.";
    }

    /// <summary>
    /// What the hook prints for <paramref name="format"/>: Claude's <c>hookSpecificOutput</c> naming
    /// the event it was called for (PostToolUse, or PostToolUseFailure after a failed tool), or
    /// Copilot's <c>additionalContext</c>; null for an agent without hooks.
    /// </summary>
    public static string? Reply(AgentFormat format, string hookInput, string text)
    {
        switch (format)
        {
            case AgentFormat.Claude:
                var output = new JsonObject
                {
                    ["hookEventName"] = EventOf(hookInput),
                    ["additionalContext"] = text,
                };
                return new JsonObject { ["hookSpecificOutput"] = output }.ToJsonString();
            case AgentFormat.Copilot:
                return new JsonObject { ["additionalContext"] = text }.ToJsonString();
            default:
                return null;
        }
    }

    /// <summary>
    /// The Claude settings that run <paramref name="exePath"/> as the hook after every tool call,
    /// failed ones too. Passed to each Claude worker with <c>--settings</c>; it adds to the
    /// developer's own settings and changes none of them.
    /// </summary>
    public static string ClaudeSettings(string exePath)
    {
        var command = $"\"{exePath.Replace('\\', '/')}\" {Switch} {FormatName(AgentFormat.Claude)}";
        JsonArray Entry() =>
        [
            new JsonObject
            {
                ["matcher"] = "",
                ["hooks"] = new JsonArray
                {
                    new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = TimeoutSeconds },
                },
            },
        ];

        var hooks = new JsonObject { [PostToolUse] = Entry(), [PostToolUseFailure] = Entry() };
        return new JsonObject { ["hooks"] = hooks }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // The event Claude called the hook for, from the hook_event_name of its input.
    private static string EventOf(string hookInput)
    {
        try
        {
            if (JsonNode.Parse(hookInput) is JsonObject input
                && input["hook_event_name"] is JsonValue value
                && value.TryGetValue<string>(out var name)
                && Text.Same(name, PostToolUseFailure))
            {
                return PostToolUseFailure;
            }
        }
        catch (JsonException)
        {
        }

        return PostToolUse;
    }
}
