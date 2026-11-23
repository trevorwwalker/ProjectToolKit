using Scheduler.Core.Contracts;

namespace Scheduler.Web.Tests.Server.Fakes;

/// <summary>A clock the test moves by hand, so the PAGE-3 rate needs no real time.</summary>
internal sealed class TestClock : IClock
{
    /// <summary>The time <see cref="Now"/> answers with.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Moves the clock on.</summary>
    public void Advance(TimeSpan by) => Now += by;
}
