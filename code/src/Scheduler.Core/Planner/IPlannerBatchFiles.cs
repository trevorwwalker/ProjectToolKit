namespace Scheduler.Core.Planner;

/// <summary>The files of one batch folder (PLAN-3, PLAN-4).</summary>
public interface IPlannerBatchFiles
{
    /// <summary>The text of <c>batch.json</c>, or null when it cannot be read.</summary>
    string? BatchJson();

    /// <summary>The text of a file in the batch folder, or null when the folder has no such file.</summary>
    string? Read(string name);
}
