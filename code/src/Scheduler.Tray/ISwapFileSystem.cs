// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>
/// The file system as a deploy needs it (967-B), so the swap and its rollback can be tested with a fake
/// file system and fake processes instead of a disk and a running exe. Paths are full paths.
/// </summary>
public interface ISwapFileSystem
{
    /// <summary>Whether the folder is there.</summary>
    bool DirectoryExists(string dir);

    /// <summary>Whether the file is there.</summary>
    bool FileExists(string file);

    /// <summary>Whether the path is a folder (a file and a missing path are both not).</summary>
    bool IsDirectory(string path);

    /// <summary>The folder's top-level entries as full paths, files and folders alike. Empty when it is not there.</summary>
    IReadOnlyList<string> Entries(string dir);

    /// <summary>Makes the folder and any folder above it.</summary>
    void CreateDirectory(string dir);

    /// <summary>Moves a file or a folder. Throws when the target is locked, as <c>File.Move</c> does.</summary>
    void Move(string from, string to);

    /// <summary>Copies a file over the target, making the folder it lands in.</summary>
    void Copy(string from, string to);

    /// <summary>Deletes a file or a folder and everything in it. A path that is not there is not an error.</summary>
    void Delete(string path);
}
