// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// A temporary repository with a bare remote for the start-up tests (CFG-1 to CFG-4): every
/// folder is under one temporary root, so nothing here touches this repository. The remote the
/// scheduler re-reads its config from is real git, wrapped so the fetches and reads are counted.
/// </summary>
internal sealed class ConfigFixture : IDisposable
{
    private readonly TempDir _temp = new();
    private TempDir? _outside;
    private readonly List<string> _fetches = [];
    private readonly List<(string Remote, string Branch, string Path)> _reads = [];

    /// <summary>What <c>%LOCALAPPDATA%</c> is for the loaded config, so tests never write to the real one.</summary>
    public string LocalAppData { get; }

    public ConfigFixture()
    {
        LocalAppData = Path.Combine(_temp.Path, "LocalAppData");
        Directory.CreateDirectory(LocalAppData);
    }

    /// <summary>The temporary root; every folder here lives under it.</summary>
    public string Root => _temp.Path;

    /// <summary>The git the host uses (CFG-16).</summary>
    public GitRunner Git { get; } = new();

    /// <summary>What the loader logged.</summary>
    public ListLog Log { get; } = new();

    /// <summary>One entry per <c>git fetch</c> attempted, "&lt;remote&gt; ok" or "&lt;remote&gt; failed".</summary>
    public IReadOnlyList<string> Fetches => _fetches;

    /// <summary>One entry per file read off the remote branch.</summary>
    public IReadOnlyList<(string Remote, string Branch, string Path)> Reads => _reads;

    /// <summary>An empty folder in %TEMP%, in no repository (the temporary root is inside this one); it writes no repositories. Removed with the fixture.</summary>
    public string OutsideFolder(string name)
    {
        _outside ??= TempDir.InSystemTemp();
        return Directory.CreateDirectory(Path.Combine(_outside.Path, name)).FullName;
    }

    /// <summary>An empty folder under the temporary root.</summary>
    public string Folder(string name) => Directory.CreateDirectory(Path.Combine(_temp.Path, name)).FullName;

    /// <summary>A new git repository with <c>main</c> checked out; a repository with no commits still has a top level.</summary>
    public string InitRepo(string name)
    {
        var dir = Folder(name);
        GitFixture.Git(dir, "init", "-q", "-b", "main");
        return dir;
    }

    /// <summary>A new bare repository to act as the remote.</summary>
    public string InitBare(string name)
    {
        var dir = Folder(name);
        GitFixture.Git(dir, "init", "-q", "--bare", "-b", "main");
        return dir;
    }

    /// <summary>Point <paramref name="dir"/> at <paramref name="url"/> under the name <paramref name="remote"/>.</summary>
    public void AddRemote(string dir, string remote, string url) => GitFixture.Git(dir, "remote", "add", remote, url);

    /// <summary>Write a file into a working tree; the path uses <c>/</c>.</summary>
    public void Write(string dir, string path, string text)
    {
        var full = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>Commit everything in a working tree.</summary>
    public void Commit(string dir, string message)
    {
        GitFixture.Git(dir, "add", "-A");
        GitFixture.Git(dir, "commit", "-q", "-m", message);
    }

    /// <summary>Push the current branch to <paramref name="branch"/> on a remote.</summary>
    public void Push(string dir, string remote, string branch) => GitFixture.Git(dir, "push", "-q", remote, "HEAD:" + branch);

    /// <summary>The top level of a repository, the way the host names it.</summary>
    public string Top(string dir) => GitFixture.Git(dir, "rev-parse", "--show-toplevel").Replace('/', '\\');

    /// <summary>Whether a fetch has left <c>&lt;remote&gt;/&lt;branch&gt;</c> in the clone.</summary>
    public bool HasRemoteBranch(string dir, string remote, string branch) =>
        Git.Run(dir, "rev-parse", "--verify", "refs/remotes/" + remote + "/" + branch).Ok;

    /// <summary>The repository the scheduler reads: real git, with the fetches and reads counted.</summary>
    public IMainRepository RemoteRepo(string repoDir, string remote, string branch) => new Counting(this, repoDir, remote, branch);

    /// <summary>A loader over this fixture's git, remote and log.</summary>
    public ConfigLoader Loader() => new(Git, RemoteRepo, Log, LocalAppData);

    public void Dispose()
    {
        _temp.Dispose();
        _outside?.Dispose();
        if (Directory.Exists(LocalAppData))
        {
            Directory.Delete(LocalAppData, true);
        }
    }

    private void RecordFetch(string remote, bool ok) => _fetches.Add(remote + (ok ? " ok" : " failed"));

    private void RecordRead(string remote, string branch, string path) => _reads.Add((remote, branch, path));

    private sealed class Counting(ConfigFixture fx, string repoDir, string remote, string branch) : IMainRepository
    {
        private readonly MainRepository _inner = new(fx.Git, repoDir, remote, branch);

        public bool Fetch()
        {
            var ok = _inner.Fetch();
            fx.RecordFetch(remote, ok);
            return ok;
        }

        public IReadOnlyList<TreeEntry>? ListTree(string folder) => _inner.ListTree(folder);

        public string? ReadBlob(string blobId) => _inner.ReadBlob(blobId);

        public string? ReadFile(string path)
        {
            fx.RecordRead(remote, branch, path);
            return _inner.ReadFile(path);
        }

        public string? HeadSha() => _inner.HeadSha();
    }
}
