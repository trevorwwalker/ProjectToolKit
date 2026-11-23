using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tick;

/// <summary>The PC's clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.Now;
}
