// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Moves;

/// <summary>
/// Opening a card from the page (MOVE-9, Open-Card/Open-Path L1547-1562): the repository's copy
/// when it has one, else the answers worktree's, reset to main first. Bringing the editor to the
/// front (Raise-Editor L1575) is not ported here: see docs/known_gaps.md.
/// </summary>
/// <param name="answers">The answers worktree.</param>
/// <param name="repo">The developer's checkout of the repository.</param>
/// <param name="launcher">Opens the file.</param>
public sealed class CardOpener(AnswersCommitter answers, string repo, IFileLauncher launcher)
{
    /// <summary>Open the file at <paramref name="path"/> (in the repository, as <c>jobs/queue/x.md</c>); "" or the error.</summary>
    public string Open(string path)
    {
        var rel = path.Replace('/', Path.DirectorySeparatorChar);
        var file = Path.Combine(repo, rel);
        if (!File.Exists(file))
        {
            file = Path.Combine(answers.AnswersTree(), rel);
        }

        if (!File.Exists(file))
        {
            return $"{path} not found";
        }

        try
        {
            launcher.Open(file);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"could not open {file}: {e.Message}";
        }

        return "";
    }
}
