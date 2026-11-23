namespace Scheduler.Core.PageModel;

/// <summary>
/// What one read of a worker's raw log produced (PAGE-3, PAGE-10): the whole lines worth showing,
/// and the line still being written.
/// </summary>
/// <param name="Partial">The streamed text so far, kept between reads.</param>
/// <param name="Lines">The raw lines the page should render, oldest first.</param>
public sealed record WorkerScanResult(string Partial, IReadOnlyList<string> Lines);
