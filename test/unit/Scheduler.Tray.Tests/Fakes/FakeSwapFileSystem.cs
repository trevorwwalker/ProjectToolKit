// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests.Fakes;

/// <summary>
/// The file system a deploy sees, held in memory (967-B): the three folders and the files a publish made,
/// with <see cref="Lock"/> naming the files a running process will not let go - which is how a test makes a
/// swap fail halfway, the way a running exe or tray does on a real machine.
/// </summary>
public sealed class FakeSwapFileSystem : ISwapFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _locked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Put a file in, with the content the test wants (a build's files carry its version).</summary>
    public FakeSwapFileSystem WithFile(string path, string content = "x")
    {
        _files[Full(path)] = content;
        var parent = Path.GetDirectoryName(Full(path));
        if (parent is not null)
        {
            _dirs.Add(parent);
        }

        return this;
    }

    /// <summary>Put a folder in.</summary>
    public FakeSwapFileSystem WithDir(string path)
    {
        _dirs.Add(Full(path));
        return this;
    }

    /// <summary>Name a file a running process holds, so moving or deleting it fails.</summary>
    public FakeSwapFileSystem Lock(string path)
    {
        _locked.Add(Full(path));
        return this;
    }

    /// <summary>The content of a file, or null when it is not there.</summary>
    public string? Content(string path) => _files.TryGetValue(Full(path), out var content) ? content : null;

    /// <summary>The file names directly inside a folder, sorted - for asserting what a swap left.</summary>
    public string[] Names(string dir) =>
        [.. _files.Keys.Where(f => Same(ParentOf(f), dir)).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)!];

    /// <inheritdoc/>
    public bool DirectoryExists(string dir) => _dirs.Contains(Full(dir));

    /// <inheritdoc/>
    public bool FileExists(string file) => _files.ContainsKey(Full(file));

    /// <inheritdoc/>
    public bool IsDirectory(string path) => _dirs.Contains(Full(path));

    /// <inheritdoc/>
    public IReadOnlyList<string> Entries(string dir)
    {
        var parent = Full(dir);
        if (!_dirs.Contains(parent))
        {
            return [];
        }

        return [.. _dirs.Where(d => Same(ParentOf(d), parent))
            .Concat(_files.Keys.Where(f => Same(ParentOf(f), parent)))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc/>
    public void CreateDirectory(string dir)
    {
        var path = Full(dir);
        for (string? at = path; at is not null && !_dirs.Contains(at); at = ParentOf(at))
        {
            _dirs.Add(at);
        }
    }

    /// <inheritdoc/>
    public void Move(string from, string to)
    {
        var source = Full(from);
        var target = Full(to);
        RefuseIfLocked(source);
        DeleteTarget(target);

        if (_dirs.Contains(source))
        {
            MoveTree(source, target);
            return;
        }

        if (!_files.TryGetValue(source, out var content))
        {
            throw new FileNotFoundException($"there is no file at {from}", from);
        }

        _files.Remove(source);
        _files[target] = content;
        var parent = Path.GetDirectoryName(target);
        if (parent is not null)
        {
            _dirs.Add(parent);
        }
    }

    /// <inheritdoc/>
    public void Copy(string from, string to)
    {
        var source = Full(from);
        var target = Full(to);
        if (!_files.TryGetValue(source, out var content))
        {
            throw new FileNotFoundException($"there is no file at {from}", from);
        }

        _files[target] = content;
        var parent = Path.GetDirectoryName(target);
        if (parent is not null)
        {
            _dirs.Add(parent);
        }
    }

    /// <inheritdoc/>
    public void Delete(string path)
    {
        var full = Full(path);
        RefuseIfLocked(full);
        if (_dirs.Contains(full))
        {
            foreach (var dir in _dirs.Where(d => Inside(d, full)).ToList())
            {
                _dirs.Remove(dir);
            }

            foreach (var file in _files.Keys.Where(f => Inside(f, full)).ToList())
            {
                _files.Remove(file);
            }

            _dirs.Remove(full);
            return;
        }

        _files.Remove(full);
    }

    private void MoveTree(string source, string target)
    {
        foreach (var dir in _dirs.Where(d => Inside(d, source)).ToList())
        {
            _dirs.Remove(dir);
            _dirs.Add(target + dir[source.Length..]);
        }

        foreach (var file in _files.Keys.Where(f => Inside(f, source)).ToList())
        {
            var content = _files[file];
            _files.Remove(file);
            _files[target + file[source.Length..]] = content;
        }

        _dirs.Remove(source);
        _dirs.Add(target);
    }

    private void DeleteTarget(string target)
    {
        if (_dirs.Contains(target))
        {
            Delete(target);
            return;
        }

        _files.Remove(target);
    }

    private void RefuseIfLocked(string path)
    {
        if (_locked.Contains(path))
        {
            throw new IOException($"The process cannot access the file '{path}' because it is being used by another process.");
        }
    }

    private static bool Inside(string path, string folder) =>
        path.Length > folder.Length
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
        && path[folder.Length] == Path.DirectorySeparatorChar;

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Full(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ParentOf(string path) => Path.GetDirectoryName(Full(path)) ?? Full(path);
}
