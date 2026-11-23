using System.Diagnostics;
using System.Text;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Seeds;

/// <summary>1061-B: git for building and checking seed repositories: bytes in and out, a timeout, the exit code checked.</summary>
internal static class SeedGit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static string Text(string dir, params string[] args) => Encoding.UTF8.GetString(Bytes(dir, null, args)).Trim();

    public static byte[] Bytes(string dir, byte[]? input, params string[] args)
    {
        var (code, output, error) = Run(dir, input, args);
        Assert.True(code == 0, $"git {string.Join(' ', args)} in {dir}: exit {code}: {error}");
        return output;
    }

    public static (int Code, byte[] Output, string Error) Run(string dir, byte[]? input, params string[] args)
    {
        var psi = new ProcessStartInfo(GitExecutable.Path)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        _ = GitExecutable.Apply(psi);
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", "-c", "safe.bareRepository=all", "-c", "gc.auto=0", "-c", "maintenance.auto=false" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var outTask = Task.Run(() =>
        {
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            return ms.ToArray();
        });
        if (input is not null)
        {
            p.StandardInput.BaseStream.Write(input);
        }

        p.StandardInput.Close();
        if (!p.WaitForExit(Timeout))
        {
            p.Kill(true);
            Assert.Fail($"git {string.Join(' ', args)}: no exit after {Timeout.TotalMinutes} min");
        }

        return (p.ExitCode, outTask.Result, err.Result);
    }

    /// <summary>A copy of the folder <paramref name="from"/> at <paramref name="to"/>: plain file copies, no git.</summary>
    public static string CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }

        return to;
    }

    /// <summary>A <c>file://</c> URL for a local folder, as git's clone wants it to use its transport (shallow, filter).</summary>
    public static string FileUrl(string path) => "file:///" + path.Replace('\\', '/');
}
