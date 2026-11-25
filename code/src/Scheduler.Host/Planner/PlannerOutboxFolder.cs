// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Planner;

namespace Scheduler.Host.Planner;

/// <summary>
/// The planner's outbox folder (PLAN-2, PLAN-8; <c>Planner-Batches</c> L1288): which batch folders
/// are there, how many wait or failed, and moving a dealt-with batch to <c>sent/</c> or
/// <c>failed/</c>, a name clash getting <c>-HHmmss</c>.
/// </summary>
public sealed class PlannerOutboxFolder
{
    private readonly string _root;

    public PlannerOutboxFolder(string root) => _root = root;

    /// <summary>The folders directly in the outbox, with whether each holds a <c>batch.json</c>.</summary>
    public IReadOnlyList<OutboxFolder> Folders() => Directory.Exists(_root)
        ? [.. Directory.EnumerateDirectories(_root).Select(d => new OutboxFolder(Path.GetFileName(d), File.Exists(Path.Combine(d, "batch.json"))))]
        : [];

    /// <summary>The complete batches, in name order (PLAN-2).</summary>
    public IReadOnlyList<string> Complete() => PlannerBatches.Complete(Folders());

    /// <summary>How many folders are in <c>failed/</c>.</summary>
    public int FailedCount()
    {
        var failed = Path.Combine(_root, "failed");
        return Directory.Exists(failed) ? Directory.EnumerateDirectories(failed).Count() : 0;
    }

    /// <summary>The files of a batch folder for Core.</summary>
    public IPlannerBatchFiles Files(string name) => new FolderBatchFiles(Path.Combine(_root, name));

    /// <summary>
    /// Moves the batch to <c>sent/</c> or <c>failed/</c> (PLAN-8), the name suffixed with the time
    /// when the target exists; a failed batch gets <c>reason.txt</c>. Returns the folder it is in
    /// now, or null (with <paramref name="error"/>) when it could not be moved and stays.
    /// </summary>
    public string? MoveOut(string name, bool failed, string reason, DateTimeOffset now, out string error)
    {
        error = "";
        var source = Path.Combine(_root, name);
        var target = Path.Combine(_root, failed ? "failed" : "sent");
        Directory.CreateDirectory(target);
        var dest = Path.Combine(target, name);
        if (Path.Exists(dest))
        {
            dest += "-" + now.ToString("HHmmss", CultureInfo.InvariantCulture);
        }

        try
        {
            Directory.Move(source, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }

        if (failed)
        {
            try
            {
                File.WriteAllText(Path.Combine(dest, "reason.txt"), reason + Environment.NewLine, new UTF8Encoding(true));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The reason is also in the log; the move is what matters.
            }
        }

        return dest;
    }

    private sealed class FolderBatchFiles(string dir) : IPlannerBatchFiles
    {
        public string? BatchJson() => Read("batch.json");

        public string? Read(string name)
        {
            try
            {
                var f = Path.Combine(dir, name);
                return File.Exists(f) ? File.ReadAllText(f) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }
}
