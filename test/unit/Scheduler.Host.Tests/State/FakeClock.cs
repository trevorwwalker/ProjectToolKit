using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tests.State;

/// <summary>The time the tests move by hand, so nothing waits for the 20 s cache or the 48 h window.</summary>
internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now = Now + by;
}
