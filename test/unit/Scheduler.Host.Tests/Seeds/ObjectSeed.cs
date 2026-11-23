using System.Text;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Seeds;

/// <summary>
/// 1061-B: a seed repository for the object reader's real-git tests, built once per test class (an
/// <c>IClassFixture</c>) and copied per test with plain file copies. One history, four ways git stores it:
/// <list type="bullet">
/// <item><c>loose</c>: every object loose; refs packed by <c>git pack-refs --all</c> (an annotated tag gives a
/// peeled line), then <c>main</c> moved on (a loose ref over a stale packed one) and <c>fresh</c> made (loose only).</item>
/// <item><c>gc</c>: after <c>git gc --aggressive --prune=now</c>: one pack, <c>OFS_DELTA</c> entries.</item>
/// <item><c>refdelta</c>: one pack written by <c>git pack-objects</c> without <c>--delta-base-offset</c>: <c>REF_DELTA</c> entries only.</item>
/// <item><c>cruft</c>: an unreachable blob and commit, then <c>git repack --cruft -d --write-midx</c>: a cruft pack
/// (with <c>.mtimes</c>) beside the main pack, and a multi-pack index.</item>
/// </list>
/// Per-repository settings are in the seed's own config (no <c>-c</c> per call): no autocrlf, no auto gc.
/// </summary>
public sealed class ObjectSeed : IDisposable
{
    public static readonly string[] Variants = ["loose", "gc", "refdelta", "cruft"];

    private readonly TempDir _temp = new();

    public ObjectSeed()
    {
        var loose = Path.Combine(_temp.Path, "loose");
        Directory.CreateDirectory(loose);
        BuildHistory(loose);

        var gc = SeedGit.CopyTree(loose, Path.Combine(_temp.Path, "gc"));
        SeedGit.Text(gc, "gc", "-q", "--aggressive", "--prune=now");

        var refDelta = SeedGit.CopyTree(loose, Path.Combine(_temp.Path, "refdelta"));
        var list = SeedGit.Bytes(refDelta, null, "rev-list", "--objects", "--all");
        SeedGit.Bytes(refDelta, list, "pack-objects", "-q", "--window=50", "--depth=50", Path.Combine(".git", "objects", "pack", "pack"));
        SeedGit.Text(refDelta, "prune-packed");

        var cruft = SeedGit.CopyTree(loose, Path.Combine(_temp.Path, "cruft"));
        UnreachableBlob = SeedGit.Bytes(cruft, Encoding.UTF8.GetBytes("nobody points at this blob\n"), "hash-object", "-w", "--stdin") is var b
            ? Encoding.ASCII.GetString(b).Trim()
            : "";
        var tree = SeedGit.Text(cruft, "rev-parse", "HEAD^{tree}");
        UnreachableCommit = SeedGit.Text(cruft, "commit-tree", tree, "-m", "dropped");
        SeedGit.Text(cruft, "repack", "-q", "--cruft", "-d", "--write-midx");
    }

    /// <summary>In the <c>cruft</c> variant: a blob and a commit nothing reaches, kept in its cruft pack.</summary>
    public string UnreachableBlob { get; }

    public string UnreachableCommit { get; }

    /// <summary>A fresh copy of <paramref name="variant"/> inside <paramref name="into"/>; the path of its working folder.</summary>
    public string Copy(string variant, string into, string name = "repo") =>
        SeedGit.CopyTree(Path.Combine(_temp.Path, variant), Path.Combine(into, name));

    public void Dispose() => _temp.Dispose();

    private static void BuildHistory(string dir)
    {
        SeedGit.Text(dir, "init", "-q", "-b", "main");
        foreach (var (k, v) in new[] { ("core.autocrlf", "false"), ("gc.auto", "0"), ("maintenance.auto", "false"), ("user.name", "Seed"), ("user.email", "seed@example.invalid"), ("commit.gpgsign", "false") })
        {
            SeedGit.Text(dir, "config", k, v);
        }

        void Write(string path, string text) => WriteBytes(path, Encoding.UTF8.GetBytes(text));
        void WriteBytes(string path, byte[] data)
        {
            var full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, data);
        }

        void Commit(string message)
        {
            SeedGit.Text(dir, "add", "-A");
            SeedGit.Text(dir, "commit", "-q", "-m", message);
        }

        string Card(int edit) => string.Concat(Enumerable.Range(0, 80).Select(i =>
            $"Line {i} of the card: the scheduler reads this text from main{(i == edit ? $", edited in step {edit}" : "")}.\n"));

        Write("README.md", "# Seed\n");
        Write("empty.txt", "");
        Write("crlf.txt", "one\r\ntwo\r\n");
        WriteBytes("binary.bin", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        Write("jobs/notes-ünï.md", "a file name in UTF-8\n");
        Write("tools/run.sh", "#!/bin/sh\necho run\n");
        Commit("first");
        SeedGit.Text(dir, "update-index", "--chmod=+x", "tools/run.sh");
        Commit("make run.sh executable");

        for (var step = 0; step < 12; step++)
        {
            Write("jobs/queue/1-A-card.md", Card(step));
            Write("docs/plan.md", Card(step * 3) + Card(step));
            Write($"jobs/done/{step + 10}-B-done.md", $"# {step + 10}-B\n\nStatus: done\n" + Card(step));
            if (step % 4 == 3)
            {
                File.Delete(Path.Combine(dir, "jobs", "done", $"{step + 8}-B-done.md"));
            }

            Commit(step == 5 ? "step 5\n\nA body\nover two lines." : $"step {step}");
        }

        SeedGit.Text(dir, "branch", "side", "HEAD~6");
        SeedGit.Text(dir, "tag", "-a", "v1", "-m", "release one", "HEAD~3");
        SeedGit.Text(dir, "tag", "light", "HEAD~2");
        SeedGit.Text(dir, "update-ref", "refs/remotes/origin/main", "HEAD~1");
        SeedGit.Text(dir, "symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
        SeedGit.Text(dir, "pack-refs", "--all");

        Write("jobs/queue/2-B-after.md", "# 2-B\n\nmade after the refs were packed\n");
        Commit("after pack-refs");
        SeedGit.Text(dir, "branch", "fresh");
    }
}
