// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
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

    /// <summary>OpenAI Codex CLI <c>exec --json</c> events (agents.md AG-2); uses its own service.</summary>
    Codex,

    /// <summary>OpenCode <c>run --format json</c> events (agents.md AG-2); uses the model endpoint unless its entry says otherwise.</summary>
    OpenCode,
}
