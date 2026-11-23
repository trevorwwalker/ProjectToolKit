namespace Scheduler.Core.Contracts;

/// <summary>How an agent's output is read (CFG-9, CFG-10).</summary>
public enum AgentFormat
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Copilot CLI JSON events.</summary>
    Copilot,

    /// <summary>Claude stream-json; uses its own service (WRK-4).</summary>
    Claude,
}
