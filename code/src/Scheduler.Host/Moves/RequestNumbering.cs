// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Host.Moves;

/// <summary>A new request and the number it was given (1493-B).</summary>
/// <param name="From">Its path as <c>{jobsDir}/requests/new-{slug}.md</c>.</param>
/// <param name="Slug">The rest of its name.</param>
/// <param name="Num">Its number, as written (three digits at least, as the requests folder has them).</param>
/// <param name="To">Its path once numbered.</param>
public sealed record NumberedRequest(string From, string Slug, string Num, string To)
{
    /// <summary><c>new-{slug}</c>, the name a card uses until it is numbered.</summary>
    public string Name => "new-" + Slug;

    /// <summary><c>{n}-{slug}</c>, its file name without <c>.md</c>.</summary>
    public string Numbered => $"{Num}-{Slug}";
}

/// <summary>
/// 1493-B: only the scheduler numbers requests, as it numbers cards (1111-B). A worker files
/// <c>{jobsDir}/requests/new-{slug}.md</c> and calls it <c>new-{slug}</c> in its card's
/// <c>waiting: planner (request new-{slug})</c> line; on main the scheduler renames it <c>{n}-{slug}.md</c>, n one more than
/// the highest number in <c>requests/</c> and <c>requests/answered/</c>, sets a <c>#NNN</c> or <c>#new</c> placeholder in its
/// title, and rewrites the cards' lines to <c>request #{n}</c> in the same commit. Pure.
/// </summary>
public static class RequestNumbers
{
    /// <summary>The <c>new-{slug}</c> of a new request at <paramref name="path"/>, or null.</summary>
    public static string? NewSlug(string jobsDir, string path)
    {
        var m = Regex.Match(path, "^" + Regex.Escape(jobsDir.Trim('/')) + @"/requests/[Nn][Ee][Ww]-([^/]+)\.md$");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>The number and slug of a numbered request in <c>requests/</c> or <c>requests/answered/</c>, or null.</summary>
    public static (long Num, string Slug)? Numbered(string jobsDir, string path)
    {
        var m = Regex.Match(path, "^" + Regex.Escape(jobsDir.Trim('/')) + @"/requests/(?:answered/)?(\d+)-([^/]+)\.md$");
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? (n, m.Groups[2].Value)
            : null;
    }

    /// <summary>The highest request number among <paramref name="paths"/>, answered ones included; 0 when none.</summary>
    public static long Highest(string jobsDir, IEnumerable<string> paths) =>
        paths.Select(p => Numbered(jobsDir, p)?.Num ?? 0).DefaultIfEmpty(0).Max();

    /// <summary>
    /// One line per number carried by more than one request file (answered ones included):
    /// <c>requests: number {n} is used by {k} files: {names}</c>, the names under <c>requests/</c>, in name order.
    /// </summary>
    public static IReadOnlyList<string> Duplicates(string jobsDir, IEnumerable<string> paths, string project)
    {
        var root = jobsDir.Trim('/') + "/requests/";
        var prefix = project.Length > 0 ? project + "/" : "";
        return paths
            .Select(p => (Path: p, Number: Numbered(jobsDir, p)))
            .Where(x => x.Number is not null)
            .GroupBy(x => x.Number!.Value.Num)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => string.Create(CultureInfo.InvariantCulture,
                $"requests: number {Format(g.Key)} is used by {g.Count()} files: {string.Join(", ", g.Select(x => prefix + x.Path[root.Length..]).Order(StringComparer.Ordinal))}"))
            .ToList();
    }

    /// <summary>
    /// The commit that numbers the new requests on main and rewrites the cards that name them, or null when there is
    /// nothing to do. <paramref name="requests"/> are the new requests, path to text; <paramref name="cards"/> the cards
    /// of <c>queue/</c> and <c>active/</c>, path to text; <paramref name="paths"/> every path on main. A card that names a
    /// <c>new-{slug}</c> already numbered on an earlier look (its move came later) is rewritten too.
    /// </summary>
    public static (JobsChange Change, IReadOnlyList<NumberedRequest> Numbered)? OnMain(string jobsDir,
        IEnumerable<KeyValuePair<string, string>> requests, IEnumerable<KeyValuePair<string, string>> cards, IReadOnlyCollection<string> paths)
    {
        var dir = jobsDir.Trim('/') + "/requests/";
        var next = Highest(jobsDir, paths);
        var numbered = new List<NumberedRequest>();
        var writes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in requests.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (NewSlug(jobsDir, path) is not { } slug)
            {
                continue;
            }

            var num = Format(++next);
            var n = new NumberedRequest(path, slug, num, $"{dir}{num}-{slug}.md");
            numbered.Add(n);
            writes[n.To] = Title(text, num);
        }

        // Every number a new-{slug} name stands for: this commit's, then those numbered before (highest wins).
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths.Where(p => NewSlug(jobsDir, p) is null))
        {
            if (Numbered(jobsDir, p) is { } x && (!known.TryGetValue(x.Slug, out var had) || long.Parse(had, CultureInfo.InvariantCulture) < x.Num))
            {
                known[x.Slug] = Format(x.Num);
            }
        }

        foreach (var n in numbered)
        {
            known[n.Slug] = n.Num;
        }

        foreach (var (path, text) in cards)
        {
            var rewritten = CardLine(text, known);
            if (!string.Equals(rewritten, text, StringComparison.Ordinal))
            {
                writes[path] = rewritten;
            }
        }

        if (writes.Count == 0)
        {
            return null;
        }

        var message = numbered.Count > 0
            ? "jobs: number " + string.Join(", ", numbered.Select(n => $"request {n.Name} as {n.Num}"))
            : "jobs: request numbers in " + string.Join(", ", writes.Keys.Select(p => p[(p.LastIndexOf('/') + 1)..^3]));
        var moves = numbered.Select(n => new FileMove { From = n.From, To = n.To }).ToList();
        return (new JobsChange { Message = message, Moves = moves, Writes = writes }, numbered);
    }

    /// <summary>The log line: <c>request numbered: {project}/new-{slug} -> {n}-{slug}</c>.</summary>
    public static string LogLine(string project, NumberedRequest request) =>
        $"request numbered: {(project.Length > 0 ? project + "/" : "")}{request.Name} -> {request.Numbered}";

    // Three digits at least, as the requests folder numbers them (028, 197).
    private static string Format(long n) => n.ToString("D3", CultureInfo.InvariantCulture);

    // The request's first heading line: a #NNN or #new placeholder becomes #{n}.
    private static string Title(string text, string num) =>
        Regex.Replace(text, @"\A(﻿?(?:[ \t]*\r?\n)*[ \t]*#[ \t]+)#(?:NNN|new)(?![A-Za-z0-9_-])", m => m.Groups[1].Value + "#" + num, RegexOptions.IgnoreCase);

    // "request new-{slug}" (with or without .md) becomes "request #{n}" for every slug whose number is known.
    private static string CardLine(string text, IReadOnlyDictionary<string, string> known) =>
        Regex.Replace(text, @"(?<=\brequest[ \t]+)new-([A-Za-z0-9_.-]+?)(?:\.md)?(?![A-Za-z0-9_.-])",
            m => known.TryGetValue(m.Groups[1].Value, out var num) ? "#" + num : m.Value, RegexOptions.IgnoreCase);
}

/// <summary>
/// 1493-B: numbers the <c>requests/new-{slug}.md</c> files a commit put on main, beside <see cref="NewCardStep"/> on the
/// repository's git writer, and at its first look logs every request number already used twice (renaming nothing).
/// </summary>
public sealed class RequestNumberStep
{
    private readonly AnswersCommitter _committer;
    private readonly string _jobsDir;
    private readonly string _project;
    private readonly ISchedulerLog _log;
    private string? _failedAt;
    private string? _lookedAt;
    private bool _checked;

    /// <param name="committer">The answers worktree the commit goes through; its model of main is read.</param>
    /// <param name="jobsDir">The project's jobs folder.</param>
    /// <param name="project">The project's name for the log, "" outside a pool.</param>
    /// <param name="log">The scheduler's log.</param>
    public RequestNumberStep(AnswersCommitter committer, string jobsDir, string project, ISchedulerLog log)
    {
        _committer = committer;
        _jobsDir = jobsDir.Trim('/');
        _project = project;
        _log = log;
        Model = () => committer.MainModel();
    }

    /// <summary>The repository's model of main (the committer's unless a test sets one).</summary>
    public Func<RepositoryModel?> Model { get; set; }

    /// <summary>One look at main; true when a numbering commit was pushed.</summary>
    public bool Run()
    {
        if (Model() is not { Snapshot: { } at } model)
        {
            return false;
        }

        var paths = at.Files.Keys.ToList();
        if (!_checked)
        {
            _checked = true;
            foreach (var line in RequestNumbers.Duplicates(_jobsDir, paths, _project))
            {
                _log.Write(line);
            }
        }

        // Main has not moved since the last look that found nothing (or failed): nothing new to do.
        if (string.Equals(_lookedAt, at.Commit, StringComparison.Ordinal) || string.Equals(_failedAt, at.Commit, StringComparison.Ordinal))
        {
            return false;
        }

        var requests = paths.Where(p => RequestNumbers.NewSlug(_jobsDir, p) is not null)
            .Select(p => KeyValuePair.Create(p, model.Text(p) ?? ""))
            .ToList();
        var folders = new[] { _jobsDir + "/queue/", _jobsDir + "/active/" };
        var cards = paths
            .Where(p => folders.Any(f => p.StartsWith(f, StringComparison.Ordinal)) && p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p, model.Text(p) ?? ""))
            .Where(kv => kv.Value.Contains("new-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (RequestNumbers.OnMain(_jobsDir, requests, cards, paths) is not { } plan)
        {
            _lookedAt = at.Commit;
            return false;
        }

        var pushed = _committer.Apply(plan.Change);
        if (!pushed.Ok)
        {
            _failedAt = at.Commit;
            _log.Write($"request numbers: could not commit {plan.Change.Message}: {pushed.Error}");
            return false;
        }

        foreach (var n in plan.Numbered)
        {
            _log.Write(RequestNumbers.LogLine(_project, n));
        }

        return true;
    }
}
