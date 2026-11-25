// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Host.Git.Objects;
using Scheduler.Host.Tests.Seeds;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Git.Objects;

/// <summary>1061-B: the in-process object reader against real git, on copies of one seed built per class (MM-1 to MM-3).</summary>
public sealed class MainObjectsTests(ObjectSeed seed) : IClassFixture<ObjectSeed>
{
    public static TheoryData<string> Variants => new(ObjectSeed.Variants);

    [Theory]
    [MemberData(nameof(Variants))]
    public void MM_1_Reader_Parity_WithCli(string variant)
    {
        using var t = new TempDir();
        var repo = seed.Copy(variant, t.Path);
        var objects = Open(repo);

        AssertObjectsMatchCli(repo, objects);
        AssertRefsMatchCli(repo, objects);
        AssertTreeMatchesCli(repo, objects, "HEAD");
        AssertTreeMatchesCli(repo, objects, "side");
        AssertCommitsMatchCli(repo, objects);
    }

    [Fact]
    public void MM_1_Reader_LooseRefWinsOverPacked_PeeledLinesSkipped_PackedOnlyFound()
    {
        using var t = new TempDir();
        var repo = seed.Copy("loose", t.Path);
        var packed = File.ReadAllLines(Path.Combine(repo, ".git", "packed-refs"));
        var packedMain = packed.Single(l => l.EndsWith(" refs/heads/main", StringComparison.Ordinal))[..40];
        var looseMain = File.ReadAllText(Path.Combine(repo, ".git", "refs", "heads", "main")).Trim();
        Assert.NotEqual(packedMain, looseMain); // the seed really has a stale packed line under a loose ref
        Assert.Contains(packed, l => l.StartsWith('^'));
        Assert.False(File.Exists(Path.Combine(repo, ".git", "refs", "tags", "v1")));

        var objects = Open(repo);
        Assert.Equal(looseMain, objects.Resolve("main")?.ToString());
        Assert.Equal(SeedGit.Text(repo, "rev-parse", "v1"), objects.Resolve("v1")?.ToString());
        Assert.Equal(SeedGit.Text(repo, "rev-parse", "refs/remotes/origin/HEAD"), objects.Resolve("origin")?.ToString());
        Assert.Null(objects.Resolve("no-such-branch"));
    }

    [Theory]
    [InlineData("gc", true, false)]
    [InlineData("refdelta", false, true)]
    public void MM_1_Reader_BothDeltaKinds(string variant, bool ofs, bool refs)
    {
        using var t = new TempDir();
        var repo = seed.Copy(variant, t.Path);
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(repo, ".git", "objects"), "*", SearchOption.AllDirectories),
            f => Path.GetFileName(Path.GetDirectoryName(f)!).Length == 2); // no loose objects: every read is from the pack
        var objects = Open(repo);
        using var s = objects.Begin();
        foreach (var id in CatAll(repo).Keys)
        {
            s.Read(ObjectId.Parse(id));
        }

        Assert.Equal(ofs, s.OfsDeltas > 0);
        Assert.Equal(refs, s.RefDeltas > 0);
    }

    [Fact]
    public void MM_1_Reader_CruftPackAndMultiPackIndex()
    {
        using var t = new TempDir();
        var repo = seed.Copy("cruft", t.Path);
        var pack = Path.Combine(repo, ".git", "objects", "pack");
        Assert.True(File.Exists(Path.Combine(pack, "multi-pack-index")));
        Assert.Single(Directory.EnumerateFiles(pack, "*.mtimes"));
        var objects = Open(repo);
        Assert.Equal(2, objects.Packs().Count);

        foreach (var id in new[] { seed.UnreachableBlob, seed.UnreachableCommit })
        {
            var cli = CatAll(repo)[id];
            var mine = objects.Read(ObjectId.Parse(id));
            Assert.Equal(cli.Type, GitObject.TypeName(mine.Type));
            Assert.Equal(cli.Data, mine.Data);
        }
    }

    [Fact]
    public void MM_1_Reader_WorktreeGitFileAndCommondir()
    {
        using var t = new TempDir();
        var repo = seed.Copy("gc", t.Path);
        var wt = Path.Combine(t.Path, "wt");
        SeedGit.Text(repo, "worktree", "add", "-q", wt, "side");
        Assert.True(File.Exists(Path.Combine(wt, ".git")));

        var objects = Open(wt);
        Assert.Equal(Path.GetFullPath(Path.Combine(repo, ".git")), objects.Folder.CommonDir);
        Assert.StartsWith(Path.Combine(objects.Folder.CommonDir, "worktrees"), objects.Folder.GitDir, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SeedGit.Text(wt, "rev-parse", "HEAD"), objects.Resolve("HEAD")?.ToString());
        Assert.Equal(SeedGit.Text(wt, "rev-parse", "main"), objects.Resolve("main")?.ToString());
        AssertRefsMatchCli(wt, objects);
        AssertTreeMatchesCli(wt, objects, "HEAD");
    }

    [Fact]
    public void MM_1_Reader_NeverWritesTheRepository()
    {
        using var t = new TempDir();
        var repo = seed.Copy("cruft", t.Path);
        var before = Snapshot(repo);
        var objects = Open(repo);
        AssertObjectsMatchCli(repo, objects);
        AssertRefsMatchCli(repo, objects);
        Assert.Equal(before, Snapshot(repo));
    }

    [Theory]
    [InlineData("sha256", "object format sha256")]
    [InlineData("reftable", "ref storage reftable")]
    [InlineData("alternates", "alternates")]
    [InlineData("shallow", "shallow")]
    [InlineData("promisor", "promisor remote")]
    [InlineData("unknown-extension", "unknown extension fancything")]
    [InlineData("format-version-2", "repository format version 2")]
    public void MM_2_Refuses_Sha256_Reftable_Alternates_Shallow_Promisor_FallsBackToCli(string kind, string reason)
    {
        using var t = new TempDir();
        var source = seed.Copy("gc", t.Path, "source");
        var repo = Path.Combine(t.Path, "repo");
        switch (kind)
        {
            case "sha256":
            case "reftable":
                Directory.CreateDirectory(repo);
                SeedGit.Text(repo, "init", "-q", "-b", "main", kind == "sha256" ? "--object-format=sha256" : "--ref-format=reftable");
                File.WriteAllText(Path.Combine(repo, "a.txt"), "a\n");
                SeedGit.Text(repo, "add", "-A");
                SeedGit.Text(repo, "commit", "-q", "-m", "one");
                break;
            case "alternates":
                SeedGit.Text(t.Path, "clone", "-q", "--shared", source, repo);
                break;
            case "shallow":
                SeedGit.Text(t.Path, "clone", "-q", "--depth", "1", SeedGit.FileUrl(source), repo);
                break;
            case "promisor":
                SeedGit.Text(source, "config", "uploadpack.allowFilter", "true");
                SeedGit.Text(t.Path, "clone", "-q", "--filter=blob:none", "--no-checkout", SeedGit.FileUrl(source), repo);
                break;
            default:
                repo = seed.Copy("gc", t.Path);
                var config = Path.Combine(repo, ".git", "config");
                var text = File.ReadAllText(config).Replace("repositoryformatversion = 0", kind == "unknown-extension" ? "repositoryformatversion = 1" : "repositoryformatversion = 2", StringComparison.Ordinal);
                File.WriteAllText(config, text + (kind == "unknown-extension" ? "[extensions]\n\tfancything = yes\n" : ""));
                break;
        }

        Assert.Null(MainObjects.TryOpen(repo, out var refusal));
        Assert.Equal(reason, refusal);

        // The fallback: a repository the reader refuses is one the git CLI reads (format version 2 aside, which
        // git refuses too and which the CLI reader then reports as it does today).
        if (kind != "format-version-2" && kind != "unknown-extension")
        {
            Assert.Matches("^[0-9a-f]{40,64}$", SeedGit.Text(repo, "rev-parse", "HEAD"));
        }
    }

    [Fact]
    public void MM_2_Accepts_APlainRepository_AndRefusesNoRepository()
    {
        using var t = new TempDir();
        var repo = seed.Copy("loose", t.Path);
        Assert.NotNull(MainObjects.TryOpen(repo, out var refusal));
        Assert.Null(refusal);
        Assert.NotNull(MainObjects.TryOpen(Path.Combine(repo, ".git"), out _));

        Assert.Null(MainObjects.TryOpen(t.Path, out refusal));
        Assert.Equal("not a git repository", refusal);
    }

    [Fact]
    public void MM_3_GcAndWorktreeRemove_NotBlockedByAnOpenModel()
    {
        using var t = new TempDir();
        var repo = seed.Copy("gc", t.Path);
        var wt = Path.Combine(t.Path, "wt");
        SeedGit.Text(repo, "worktree", "add", "-q", wt, "side");

        var objects = Open(repo);
        var inWorktree = Open(wt);
        AssertTreeMatchesCli(repo, objects, "HEAD");
        AssertTreeMatchesCli(wt, inWorktree, "HEAD");
        var oldPacks = objects.Packs().Select(p => p.PackPath).ToList();
        Assert.NotEmpty(oldPacks);

        // A new commit, then a gc that writes a new pack and deletes the old one while both models exist.
        File.WriteAllText(Path.Combine(repo, "after-gc.txt"), "written after the model read the packs\n");
        SeedGit.Text(repo, "add", "-A");
        SeedGit.Text(repo, "commit", "-q", "-m", "before gc");
        SeedGit.Text(repo, "gc", "-q", "--prune=now");
        Assert.All(oldPacks, p => Assert.False(File.Exists(p), $"{p} still there after gc"));

        // The next read finds the new pack by itself (the list is read again when an id is not found).
        Assert.Equal(SeedGit.Text(repo, "rev-parse", "HEAD"), objects.Resolve("HEAD")?.ToString());
        AssertTreeMatchesCli(repo, objects, "HEAD");
        AssertTreeMatchesCli(wt, inWorktree, "HEAD");

        SeedGit.Text(repo, "worktree", "remove", wt);
        Assert.False(Directory.Exists(wt));
    }

    [Fact]
    public void MM_1_Reader_ByteOrderMark_SameTextAsTheCliReader()
    {
        using var fx = new GitFixture();
        var files = new Dictionary<string, string?>
        {
            ["jobs/queue/900-A-bom.md"] = "\uFEFF# 900-A: bom\n\nCategory: A\nStatus: ready\n",
            ["jobs/requests/1-bom.md"] = "\uFEFF# Request 1\n\nStatus: open\n",
            ["jobs/scheduler.json"] = "\uFEFF{\"maxMinutes\": 45}\n",
        };
        // File.WriteAllText writes UTF-8 with no mark of its own, so the U+FEFF above is the mark.
        fx.Push("bom files", files);
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");

        var objects = Open(fx.Repo);
        var source = new Scheduler.Host.Git.MainObjectsSource(objects);
        var cli = fx.Main();
        foreach (var path in files.Keys)
        {
            var blob = GitFixture.Git(fx.Repo, "rev-parse", $"HEAD:{path}");
            var fromCli = cli.ReadBlob(blob);
            var inProcess = source.ReadBlob(blob);
            Assert.NotNull(fromCli);
            Assert.Equal(fromCli, inProcess);
            Assert.False(inProcess!.StartsWith('\uFEFF'), path);
        }
    }

    private static MainObjects Open(string repo)
    {
        var objects = MainObjects.TryOpen(repo, out var refusal);
        Assert.True(objects is not null, $"refused: {refusal}");
        return objects!;
    }

    private static Dictionary<string, (string Type, byte[] Data)> CatAll(string repo)
    {
        var raw = SeedGit.Bytes(repo, null, "cat-file", "--batch-all-objects", "--batch");
        var all = new Dictionary<string, (string, byte[])>(StringComparer.Ordinal);
        var at = 0;
        while (at < raw.Length)
        {
            var nl = Array.IndexOf(raw, (byte)'\n', at);
            var head = Encoding.ASCII.GetString(raw, at, nl - at).Split(' ');
            var size = int.Parse(head[2], CultureInfo.InvariantCulture);
            all[head[0]] = (head[1], raw.AsSpan(nl + 1, size).ToArray());
            at = nl + 1 + size + 1;
        }

        return all;
    }

    private static void AssertObjectsMatchCli(string repo, MainObjects objects)
    {
        var cli = CatAll(repo);
        Assert.True(cli.Count > 50, $"only {cli.Count} objects in the seed");
        using var s = objects.Begin();
        foreach (var (id, (type, data)) in cli)
        {
            var mine = s.Read(ObjectId.Parse(id));
            Assert.True(type == GitObject.TypeName(mine.Type), $"{id}: git says {type}, the reader {mine.Type}");
            Assert.True(data.AsSpan().SequenceEqual(mine.Data), $"{id} ({type}): content differs");
        }
    }

    private static void AssertRefsMatchCli(string repo, MainObjects objects)
    {
        var cli = SeedGit.Text(repo, "for-each-ref", "--format=%(objectname) %(refname)")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        var mine = objects.Refs.List().Select(r => $"{r.Value} {r.Key}").Order(StringComparer.Ordinal);
        Assert.Equal(cli, mine);
        foreach (var name in new[] { "HEAD", "main", "side", "v1", "light", "fresh", "origin/main", "origin", "refs/heads/main" })
        {
            Assert.Equal(SeedGit.Text(repo, "rev-parse", name), objects.Resolve(name)?.ToString());
        }
    }

    private static void AssertTreeMatchesCli(string repo, MainObjects objects, string rev)
    {
        var cli = Encoding.UTF8.GetString(SeedGit.Bytes(repo, null, "ls-tree", "-r", "-t", "-z", "--full-tree", rev))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        using var s = objects.Begin();
        var root = objects.Resolve(rev) ?? throw new InvalidOperationException(rev);
        var mine = new List<string>();
        Walk(s, s.TreeOf(root), "", mine);
        Assert.Equal(cli, mine);
        foreach (var path in new[] { "jobs/queue/1-A-card.md", "jobs/notes-ünï.md", "crlf.txt" })
        {
            var entry = s.FindPath(root, path);
            Assert.NotNull(entry);
            Assert.Equal(SeedGit.Bytes(repo, null, "cat-file", "blob", $"{rev}:{path}"), s.ReadBlob(entry.Id));
        }

        Assert.Null(s.FindPath(root, "jobs/queue/nothing.md"));
        Assert.Null(s.FindPath(root, "README.md/below-a-blob"));
    }

    private static void Walk(ObjectReadSession s, ObjectId tree, string prefix, List<string> lines)
    {
        foreach (var e in s.ReadTree(tree))
        {
            var kind = e.Kind switch { TreeEntryKind.Tree => "tree", TreeEntryKind.Commit => "commit", _ => "blob" };
            lines.Add($"{e.Mode.PadLeft(6, '0')} {kind} {e.Id}\t{prefix}{e.Name}");
            if (e.Kind == TreeEntryKind.Tree)
            {
                Walk(s, e.Id, prefix + e.Name + "/", lines);
            }
        }
    }

    private static void AssertCommitsMatchCli(string repo, MainObjects objects)
    {
        var cli = SeedGit.Text(repo, "log", "--all", "--format=%H%x01%T%x01%P%x01%ct%x01%at%x01%an <%ae>%x01%s")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(cli.Length >= 14);
        using var s = objects.Begin();
        foreach (var line in cli)
        {
            var f = line.Split('\x01');
            var c = s.ReadCommit(ObjectId.Parse(f[0]));
            Assert.Equal(f[1], c.Tree.ToString());
            Assert.Equal(f[2], string.Join(' ', c.Parents));
            Assert.Equal(f[3], c.CommitTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            Assert.Equal(f[4], c.AuthorTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            Assert.StartsWith(f[5] + " ", c.Author, StringComparison.Ordinal);
            Assert.Equal(f[6], c.Subject);
        }
    }

    private static List<string> Snapshot(string repo) =>
        Directory.EnumerateFileSystemEntries(Path.Combine(repo, ".git"), "*", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            .Select(i => $"{Path.GetRelativePath(repo, i.FullName)} {(i.Exists ? i.Length : -1)} {i.LastWriteTimeUtc.Ticks}")
            .Order(StringComparer.Ordinal)
            .ToList();
}
