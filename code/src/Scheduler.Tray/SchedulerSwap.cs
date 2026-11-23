namespace Scheduler.Tray;

/// <summary>
/// The swap of a deploy (967-B): the published <c>scheduler.next</c> becomes <c>scheduler</c> and the build
/// it replaces is kept in <c>scheduler.prev</c>, so a new build that will not start can be put back.
/// It is done entry by entry rather than by renaming the two folders, because the tray may be running from
/// the scheduler's own folder: every name in <c>keepInCurrent</c> is left exactly where it is (the tray's
/// exe and dll, which Windows will not let go while it runs). Nothing moves the running path, so the exe the
/// page's firewall rule is bound to (BUILD-3) still matches afterwards.
/// </summary>
public static class SchedulerSwap
{
    /// <summary>
    /// Put <c>scheduler.next</c> into <c>scheduler</c>, keeping the build it replaces in <c>scheduler.prev</c>.
    /// A file that will not move puts back what had already been moved and fails the swap, leaving the
    /// running folder exactly as it was.
    /// </summary>
    public static SwapOutcome Run(ISwapFileSystem fs, DeployLayout layout, IReadOnlyList<string> keepInCurrent)
    {
        var keep = new HashSet<string>(keepInCurrent, StringComparer.OrdinalIgnoreCase);
        if (!fs.DirectoryExists(layout.Next))
        {
            return new(false, $"there is nothing to deploy: {layout.Next} is not there");
        }

        if (!fs.FileExists(layout.NextSchedulerExe))
        {
            return new(false, $"there is nothing to deploy: {layout.Next} has no {DeployLayout.SchedulerExeName} in it");
        }

        if (!fs.DirectoryExists(layout.Current))
        {
            return new(false, $"{layout.Current} is not there; publish the scheduler before updating it");
        }

        if (fs.DirectoryExists(layout.Prev))
        {
            fs.Delete(layout.Prev);   // what an earlier deploy left behind
        }

        fs.CreateDirectory(layout.Prev);
        var swappingTheNewBuildIn = false;
        try
        {
            foreach (var entry in fs.Entries(layout.Current))
            {
                var name = Path.GetFileName(entry);
                if (keep.Contains(name))
                {
                    continue;
                }

                fs.Move(entry, Path.Combine(layout.Prev, name));
            }

            swappingTheNewBuildIn = true;
            foreach (var entry in fs.Entries(layout.Next))
            {
                var name = Path.GetFileName(entry);
                if (keep.Contains(name))
                {
                    continue;   // the tray's own files, which this running tray is using
                }

                fs.Move(entry, Path.Combine(layout.Current, name));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var putBack = PutBack(fs, layout, keep, swappingTheNewBuildIn);
            var how = putBack.Ok
                ? "the build that was running is in place again"
                : $"and the build that was running could not be put back ({putBack.Message})";
            return new(false, $"the swap went wrong: {e.Message}; {how}");
        }

        fs.Delete(layout.Next);
        return new(true, $"the new build is in {layout.Current}; the build it replaced is in {layout.Prev}");
    }

    /// <summary>
    /// Put the build kept in <c>scheduler.prev</c> back into <c>scheduler</c> - the rollback of a new build
    /// that would not start. Whatever the failed deploy left in the running folder goes first.
    /// </summary>
    public static SwapOutcome RollBack(ISwapFileSystem fs, DeployLayout layout, IReadOnlyList<string> keepInCurrent) =>
        PutBack(fs, layout, new HashSet<string>(keepInCurrent, StringComparer.OrdinalIgnoreCase), newBuildArrived: true);

    // When the failure came at the second loop the running folder holds a build that has to be cleared out
    // of the way first; when it came at the first, the old build is still lying there in pieces and needs
    // only the part that had already moved put back.
    private static SwapOutcome PutBack(ISwapFileSystem fs, DeployLayout layout, HashSet<string> keep, bool newBuildArrived)
    {
        if (!fs.DirectoryExists(layout.Prev))
        {
            return new(false, $"{layout.Prev} is not there; there is no build to put back");
        }

        try
        {
            if (newBuildArrived)
            {
                foreach (var entry in fs.Entries(layout.Current))
                {
                    var name = Path.GetFileName(entry);
                    if (keep.Contains(name) || fs.FileExists(Path.Combine(layout.Prev, name)))
                    {
                        continue;   // the tray's own, and anything the build being put back brings with it
                    }

                    fs.Delete(entry);
                }
            }

            foreach (var entry in fs.Entries(layout.Prev))
            {
                var target = Path.Combine(layout.Current, Path.GetFileName(entry));
                if (fs.DirectoryExists(target))
                {
                    fs.Delete(target);   // a folder cannot be moved onto one that is there
                }

                fs.Move(entry, target);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, e.Message);
        }

        fs.Delete(layout.Prev);
        return new(true, $"{Path.GetFileName(layout.Prev)} is back in {layout.Current}");
    }
}
