// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Launch;

/// <summary>The files a worker writes to and the sentence that points at its brief (WRK-9).</summary>
public sealed record WorkerLogFiles
{
    /// <summary>The raw log: the <c>.jsonl</c> for a json format, the <c>.log</c> for text.</summary>
    public required string RawLog { get; init; }

    /// <summary>The readable log: the <c>.log</c> beside the raw one, or the raw one itself for text.</summary>
    public required string View { get; init; }

    /// <summary>The file the brief is written to, beside the log.</summary>
    public required string PromptFile { get; init; }

    /// <summary>What the command line passes instead of the brief.</summary>
    public required string Pointer { get; init; }
}
