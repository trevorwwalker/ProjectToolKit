namespace Scheduler.Core.Contracts;

/// <summary>An agent: the default one from <c>worker.*</c> (CFG-9) or <c>cfg.agents.&lt;name&gt;</c> (CFG-10).</summary>
public sealed record AgentDefinition
{
    /// <summary>The default agent's name (CFG-11).</summary>
    public const string DefaultName = "default";

    /// <summary>The agent's name.</summary>
    public string Name { get; init; } = DefaultName;

    /// <summary>The command; for a named agent the default is its name.</summary>
    public string Cli { get; init; } = "copilot";

    /// <summary>The arguments, with placeholders (CFG-12).</summary>
    public string Args { get; init; } = "-p {prompt} --allow-all --no-ask-user";

    /// <summary>Environment variables to set, with placeholders (WRK-8).</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } =
        new Dictionary<string, string>(Text.Comparer);

    /// <summary>Patterns that mean the account is out of quota (TIME-15).</summary>
    public IReadOnlyList<string> QuotaPatterns { get; init; } = [];

    /// <summary>The model, or "".</summary>
    public string Model { get; init; } = "";

    /// <summary>How its output is read.</summary>
    public AgentFormat Format { get; init; } = AgentFormat.Text;

    /// <summary>The config has an <c>endpoint</c> key for it, even "" (CFG-10, WRK-4).</summary>
    public bool HasEndpoint { get; init; }

    /// <summary>The endpoint's value, or "".</summary>
    public string Endpoint { get; init; } = "";
}
