using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>The time the tests move by hand, so no test waits for a real clock.</summary>
internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now = Now + by;
}
