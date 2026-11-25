// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Web.Tests.Views.Fakes;

/// <summary>The scheduler's log, kept in memory so a test can read what was logged (MOVE-11).</summary>
internal sealed class RecordingSchedulerLog : ISchedulerLog
{
    /// <summary>Every line written, oldest first.</summary>
    public List<string> Lines { get; } = [];

    public void Write(string line) => Lines.Add(line);
}
