// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>The marks a rendered worker-output line starts with (PAGE-10).</summary>
public enum LiveMark
{
    /// <summary>Reasoning, shown as <c>~</c>.</summary>
    Reasoning,

    /// <summary>Message, shown as <c>*</c>.</summary>
    Message,

    /// <summary>Tool call, shown as <c>&gt;</c>.</summary>
    ToolCall,

    /// <summary>Failed tool, shown as <c>x</c>.</summary>
    FailedTool,

    /// <summary>A model call that took over 30 s.</summary>
    SlowModelCall,

    /// <summary>Error event, shown as <c>!</c>.</summary>
    Error,

    /// <summary>Session end with cost (Claude stream-json).</summary>
    SessionEnd,
}
