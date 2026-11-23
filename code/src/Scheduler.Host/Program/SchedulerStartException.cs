namespace Scheduler.Host.Program;

/// <summary>Start-up cannot go on (CFG-14: another scheduler runs; CFG-17: no address to listen on).</summary>
public sealed class SchedulerStartException(string message) : Exception(message);
