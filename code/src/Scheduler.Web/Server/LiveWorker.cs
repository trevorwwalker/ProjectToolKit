namespace Scheduler.Web.Server;

/// <summary>One running worker as the live view sees it (PAGE-3): the log it writes and the two
/// lines beside it, which the host already has text for.</summary>
/// <param name="Slot">The worker's folder number, from 1 (WT-W3).</param>
/// <param name="LogPath">The log file the worker is writing.</param>
/// <param name="Stats">The stats line (PAGE-10).</param>
/// <param name="Time">The elapsed time line.</param>
/// <param name="Project">
/// The project the worker runs a card of. Empty is the single project the scheduler serves; in a
/// pool it is what tells two projects' slot 1 apart (WT-W3), and the live view keys each worker by
/// the pair.
/// </param>
public sealed record LiveWorker(int Slot, string LogPath, string Stats, string Time, string Project = "");
