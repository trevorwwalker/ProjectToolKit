using System.Security.AccessControl;
using System.Security.Principal;
using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Config;

/// <summary>
/// Finds the git repository a folder belongs to (CFG-1). Since 1065-B (MM-14) it walks up to the
/// <c>.git</c> folder or file in process when the answer is plain - a working folder of a repository the
/// object reader can open, owned by this user, with no link on the way and no git variable that moves
/// the search; anything else is still asked of git, so a folder is a repository only when git would say so.
/// </summary>
public sealed class RepositoryLocator(GitRunner git)
{
    private static readonly string[] SearchVariables =
        ["GIT_DIR", "GIT_WORK_TREE", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM", "GIT_COMMON_DIR"];

    /// <summary>The top level of the repository containing <paramref name="folder"/>, or null when git knows none.</summary>
    public string? TopLevel(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        if (InProcess(folder) is { } found)
        {
            return found;
        }

        var r = git.Run(folder, "rev-parse", "--show-toplevel");
        if (!r.Ok)
        {
            return null;
        }

        // The first line of git's answer, as PowerShell took it (L114).
        var top = r.Stdout.Split('\n')[0].Trim().Replace('/', '\\');
        return top.Length == 0 ? null : top;
    }

    /// <summary>
    /// What <c>rev-parse --show-toplevel</c> answers, found in process: the nearest folder at or above
    /// <paramref name="folder"/> with a <c>.git</c> folder or file, spelt as it is on disk; null when git must be
    /// asked (not Windows, a git variable set, a link or network path, inside a git folder or a bare repository,
    /// a git folder the reader refuses, a folder this user does not own, or nothing found).
    /// </summary>
    public static string? InProcess(string folder)
    {
        if (!OperatingSystem.IsWindows() || SearchVariables.Any(v => Environment.GetEnvironmentVariable(v) is { Length: > 0 }))
        {
            return null;
        }

        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.Length < 2 || full[1] != ':')
            {
                return null;
            }

            for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent)
            {
                if (string.Equals(dir.Name, ".git", StringComparison.OrdinalIgnoreCase)
                    || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || (File.Exists(Path.Combine(dir.FullName, "HEAD")) && Directory.Exists(Path.Combine(dir.FullName, "objects"))))
                {
                    return null; // inside a git folder, a link, or a bare repository: git decides
                }

                var dotGit = Path.Combine(dir.FullName, ".git");
                if (!Directory.Exists(dotGit) && !File.Exists(dotGit))
                {
                    continue;
                }

                var (gitFolder, refusal) = GitFolder.Find(dir.FullName);
                return gitFolder is null || refusal is not null || dir.Parent is null || !Owned(dir) ? null : OnDisk(dir);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        return null;
    }

    // git's safe.directory check passes for a folder its user owns; any other owner is left to git and its config.
    private static bool Owned(DirectoryInfo dir)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var owner = dir.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        using var me = WindowsIdentity.GetCurrent();
        return owner is not null && owner.Equals(me.User);
    }

    // The folder spelt as it is on disk (git's real path): each name as the folder lists it, the drive in capitals.
    private static string OnDisk(DirectoryInfo dir)
    {
        var names = new Stack<string>();
        for (var at = dir; at.Parent is { } parent; at = parent)
        {
            names.Push(parent.EnumerateDirectories(at.Name).FirstOrDefault()?.Name ?? at.Name);
        }

        var root = dir.Root.FullName.TrimEnd('\\');
        return string.Join('\\', [char.ToUpperInvariant(root[0]) + root[1..], .. names]);
    }
}
