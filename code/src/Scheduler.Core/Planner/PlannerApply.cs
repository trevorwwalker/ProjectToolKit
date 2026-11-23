using System.Text.Json;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Planner;

/// <summary>
/// One batch applied to main, in memory (PLAN-3, PLAN-4, PLAN-5; <c>Apply-Planner-Batch</c>
/// L1346): validation, then writes, moves and removals in that order, answering with the change to
/// commit or why the batch fails. Host resets the answers worktree, calls this, commits and pushes
/// (PLAN-6), and calls again from the new main when the push is rejected.
/// </summary>
public static class PlannerApply
{
    /// <summary>Applies the batch to <paramref name="main"/>.</summary>
    /// <param name="batch">The batch folder.</param>
    /// <param name="main">Main as it is now.</param>
    /// <param name="jobsDir">The jobs folder in the repository, without a trailing <c>/</c>.</param>
    /// <param name="branch">The main branch's name, for the messages.</param>
    public static PlannerPlan Plan(IPlannerBatchFiles batch, IPlannerMain main, string jobsDir, string branch)
    {
        var parsed = Parse(batch.BatchJson(), out var error);
        if (parsed is null)
        {
            return PlannerPlan.Fail(error);
        }

        var message = parsed.Message;
        if (message.Length == 0)
        {
            return PlannerPlan.Fail("batch.json has no message");
        }

        var full = parsed.Trailers.Count > 0 ? message + "\n\n" + string.Join("\n", parsed.Trailers) : message;
        if (parsed.Ops.Count == 0)
        {
            return PlannerPlan.Fail("batch.json has no ops", message);
        }

        var invalid = Validate(parsed.Ops, batch, jobsDir + "/");
        if (invalid.Length > 0)
        {
            return PlannerPlan.Fail(invalid, message);
        }

        var run = new Run(batch, main, jobsDir, branch);
        var failure = run.Execute(parsed.Ops);
        if (failure.Length > 0)
        {
            return PlannerPlan.Fail(failure, message);
        }

        return new PlannerPlan
        {
            Ok = true,
            Message = message,
            CommitMessage = full,
            Change = run.Change(message),
            Note = string.Join("; ", run.Notes),
            Written = run.Written(),
        };
    }

    private sealed record Op(string Kind, string Path, string From, string To, string Base, string File);

    private sealed record Parsed(string Message, List<string> Trailers, List<Op> Ops);

    private static Parsed? Parse(string? json, out string error)
    {
        error = "batch.json is not valid JSON";
        if (json is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var message = Str(root, "message").Trim().Replace('"', '\'');
            var trailers = new List<string>();
            if (Prop(root, "trailers") is { } tr)
            {
                foreach (var item in Items(tr))
                {
                    var s = Text(item);
                    if (Truthy(item))
                    {
                        trailers.Add(s.Trim().Replace('"', '\''));
                    }
                }
            }

            var ops = new List<Op>();
            if (Prop(root, "ops") is { } os)
            {
                foreach (var o in Items(os))
                {
                    if (!Truthy(o))
                    {
                        continue;
                    }

                    ops.Add(new Op(Str(o, "op"), Str(o, "path"), Str(o, "from"), Str(o, "to"), Str(o, "base"), Str(o, "file")));
                }
            }

            return new Parsed(message, trailers, ops);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // PowerShell's property lookup ignores case; a missing property is $null.
    private static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var p in e.EnumerateObject())
        {
            if (Contracts.Text.Same(p.Name, name))
            {
                return p.Value;
            }
        }

        return null;
    }

    private static string Str(JsonElement e, string name) => Prop(e, name) is { } v ? Text(v) : "";

    private static string Text(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => v.GetRawText(),
    };

    private static IEnumerable<JsonElement> Items(JsonElement v) =>
        v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [v];

    private static bool Truthy(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => false,
        JsonValueKind.String => v.GetString() is { Length: > 0 },
        JsonValueKind.Number => v.GetRawText() != "0",
        _ => true,
    };

    // Every path under the jobs folder, and every file the batch names inside the batch (PLAN-3).
    private static string Validate(List<Op> ops, IPlannerBatchFiles batch, string prefix)
    {
        static bool Ok(string p, string prefix) =>
            p.StartsWith(prefix, StringComparison.Ordinal)
            && !Contracts.Text.IsMatch(p, @"(^|/)\.\.(/|$)")
            && !Contracts.Text.IsMatch(p, @"[\\:]");

        foreach (var o in ops)
        {
            if (Contracts.Text.Same(o.Kind, "write"))
            {
                if (!Ok(o.Path, prefix))
                {
                    return $"write to {o.Path}: only files under {prefix} can be written";
                }

                if (o.File.Length == 0 || o.File.Contains("..", StringComparison.Ordinal) || batch.Read(o.File) is null)
                {
                    return $"write to {o.Path}: its file {o.File} is not in the batch";
                }

                if (o.Base.Length == 0)
                {
                    return $"write to {o.Path}: no base";
                }
            }
            else if (Contracts.Text.Same(o.Kind, "move"))
            {
                if (!Ok(o.From, prefix) || !Ok(o.To, prefix))
                {
                    return $"move {o.From} to {o.To}: only files under {prefix} can be moved";
                }
            }
            else if (Contracts.Text.Same(o.Kind, "remove"))
            {
                if (!Ok(o.Path, prefix))
                {
                    return $"remove {o.Path}: only files under {prefix} can be removed";
                }
            }
            else
            {
                return $"unknown op '{o.Kind}'";
            }
        }

        return "";
    }

    // The worktree as the batch changes it: main's files plus what the ops have done so far.
    private sealed class Run(IPlannerBatchFiles batch, IPlannerMain main, string jobsDir, string branch)
    {
        private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
        private readonly HashSet<string> _present = new(main.Paths(), StringComparer.Ordinal);
        private readonly Dictionary<string, string> _where = new(StringComparer.Ordinal);
        private readonly List<FileMove> _moves = [];
        private readonly List<string> _removals = [];
        private readonly HashSet<string> _writes = new(StringComparer.Ordinal);
        private readonly List<string> _order = [];
        private readonly List<string> _writeOrder = [];

        public List<string> Notes { get; } = [];

        public string Execute(List<Op> ops)
        {
            foreach (var o in ops.Where(o => Contracts.Text.Same(o.Kind, "write")))
            {
                var failure = Write(o);
                if (failure.Length > 0)
                {
                    return failure;
                }
            }

            foreach (var o in ops.Where(o => Contracts.Text.Same(o.Kind, "move")))
            {
                var failure = Move(o.From, o.To);
                if (failure.Length > 0)
                {
                    return failure;
                }
            }

            foreach (var o in ops.Where(o => Contracts.Text.Same(o.Kind, "remove")))
            {
                if (!_present.Contains(o.Path))
                {
                    Notes.Add($"{o.Path} was already gone");
                    continue;
                }

                Delete(o.Path);
            }

            return "";
        }

        public JobsChange Change(string message) => new()
        {
            Message = message,
            Moves = _moves,
            Writes = _writeOrder.Where(_writes.Contains).ToDictionary(p => p, p => _texts[p], StringComparer.Ordinal),
            Removals = _removals,
        };

        public List<WrittenFile> Written() =>
            [.. _order.Select(p => _where[p]).Where(_present.Contains).Select(p => new WrittenFile(p, Current(p)))];

        private string Current(string path) => _texts.TryGetValue(path, out var t) ? t : main.ReadFile(path) ?? "";

        private string Write(Op o)
        {
            var rel = o.Path;
            var isNew = Contracts.Text.Same(o.Base, "new");

            // The card moved since the planner read it: write it where it is now.
            if (!isNew && !_present.Contains(rel) && Find(rel) is { Length: > 0 } now)
            {
                Notes.Add($"{rel} had moved to {now}: written there");
                rel = now;
            }

            _where[o.Path] = rel;
            _order.Add(o.Path);
            var mine = batch.Read(o.File) ?? "";
            var curSha = main.BlobId(rel);
            if (isNew)
            {
                if (curSha is not null)
                {
                    if (PlannerMerge.ToLf(Current(rel)).TrimEnd() != PlannerMerge.ToLf(mine).TrimEnd())
                    {
                        return $"{rel} is already on {branch} (the batch writes it as new)";
                    }

                    Notes.Add($"{rel} was already on {branch} as written");
                    return "";
                }

                Put(rel, mine);
            }
            else if (curSha is null)
            {
                return $"{rel} is no longer on {branch} (moved or removed since the planner read it)";
            }
            else if (Contracts.Text.Same(curSha, o.Base))
            {
                Put(rel, PlannerMerge.LikeEol(mine, Current(rel)));
            }
            else
            {
                var baseText = main.ReadBlob(o.Base);
                var merged = baseText is null ? null : PlannerMerge.Merge(baseText, Current(rel), mine);
                if (merged is null)
                {
                    return $"{rel} changed on {branch} since the planner read it, and the changes conflict";
                }

                Put(rel, merged);
                Notes.Add($"{rel} merged with {branch}'s newer copy");
            }

            return "";
        }

        private void Put(string path, string text)
        {
            _texts[path] = text;
            _present.Add(path);
            _writes.Add(path);
            if (!_writeOrder.Contains(path))
            {
                _writeOrder.Add(path);
            }
        }

        private string Move(string from, string to)
        {
            if (Contracts.Text.Same(from, to))
            {
                Notes.Add($"move of {from} to itself skipped");
                return "";
            }

            // The card moved since the planner read it: move it from where it is now.
            if (!_present.Contains(from) && !_present.Contains(to) && Find(from) is { Length: > 0 } now && !Contracts.Text.Same(now, to))
            {
                Notes.Add($"{from} had moved to {now}: moved from there");
                from = now;
            }

            var hasFrom = _present.Contains(from);
            var hasTo = _present.Contains(to);
            if (hasTo && !hasFrom)
            {
                Notes.Add($"{to} is already there: move skipped");
                return "";
            }

            if (hasTo)
            {
                return $"both {from} and {to} are on {branch}";
            }

            if (!hasFrom)
            {
                return $"{from} is not on {branch}";
            }

            // A file this batch created leaves no move behind: it is just written where it ends up.
            var created = _writes.Contains(from) && main.BlobId(from) is null;
            if (!created)
            {
                _moves.Add(new FileMove { From = from, To = to });
            }

            if (_texts.TryGetValue(from, out var text))
            {
                _texts.Remove(from);
                _texts[to] = text;
                _writes.Remove(from);
                _writes.Add(to);
                _writeOrder[_writeOrder.IndexOf(from)] = to;
            }
            else
            {
                _texts.Remove(to);
            }

            _present.Remove(from);
            _present.Add(to);
            foreach (var k in _where.Keys.ToList().Where(k => _where[k] == from))
            {
                _where[k] = to;
            }

            return "";
        }

        private void Delete(string path)
        {
            if (_writes.Remove(path))
            {
                _writeOrder.Remove(path);
                _texts.Remove(path);
            }

            if (main.BlobId(path) is not null)
            {
                _removals.Add(path);
            }

            _present.Remove(path);
        }

        // Where a job file is on main now when it is no longer where the planner read it: the same
        // file name in another jobs folder (PLAN-4, Planner-Find L1335). "" if none.
        private string Find(string rel)
        {
            var leaf = rel[(rel.LastIndexOf('/') + 1)..];
            foreach (var p in main.Paths())
            {
                if (!p.StartsWith(jobsDir + "/", StringComparison.Ordinal) || Contracts.Text.Same(p, rel))
                {
                    continue;
                }

                if (Contracts.Text.Same(p[(p.LastIndexOf('/') + 1)..], leaf))
                {
                    return p;
                }
            }

            return "";
        }
    }
}
