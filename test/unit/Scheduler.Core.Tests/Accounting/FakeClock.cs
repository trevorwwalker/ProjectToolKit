// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>The time the tests move by hand, so no test waits for a real clock.</summary>
internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now = Now + by;
}
