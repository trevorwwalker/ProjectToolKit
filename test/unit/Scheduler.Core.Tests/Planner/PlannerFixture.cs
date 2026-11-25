// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Core.Planner;

namespace Scheduler.Core.Tests.Planner;

/// <summary>Main's files and a batch folder in memory, for the planner tests (A11). Test plan: scheduler-next/test-plans/planner.md.</summary>
internal sealed class PlannerFixture : IPlannerMain, IPlannerBatchFiles
{
    public const string Jobs = "docs/jobs";

    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _blobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _batchFiles = new(StringComparer.Ordinal);

    public string? BatchJsonText { get; set; }

    /// <summary>A file on main.</summary>
    public PlannerFixture Main(string path, string text)
    {
        _files[path] = text;
        Blob(text);
        return this;
    }

    /// <summary>An older copy of a file: its blob id, readable by <see cref="ReadBlob"/>.</summary>
    public string Blob(string text)
    {
        var id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        _blobs[id] = text;
        return id;
    }

    /// <summary>A file in the batch folder.</summary>
    public PlannerFixture InBatch(string name, string text)
    {
        _batchFiles[name] = text;
        return this;
    }

    /// <summary>Sets batch.json from a message and ops (each op an anonymous object).</summary>
    public PlannerFixture Batch(string message, params object[] ops)
    {
        BatchJsonText = JsonSerializer.Serialize(new { message, trailers = new[] { "Co-Authored-By: P <p@x>" }, ops });
        return this;
    }

    public PlannerPlan Plan() => PlannerApply.Plan(this, this, Jobs, "main");

    public IReadOnlyList<string> Paths() => [.. _files.Keys];

    public string? BlobId(string path) => _files.TryGetValue(path, out var t) ? Blob(t) : null;

    public string? ReadFile(string path) => _files.GetValueOrDefault(path);

    public string? ReadBlob(string blobId) => _blobs.GetValueOrDefault(blobId);

    public string? BatchJson() => BatchJsonText;

    public string? Read(string name) => _batchFiles.GetValueOrDefault(name);
}

internal sealed class ListLog : ISchedulerLog
{
    public List<string> Lines { get; } = [];

    public void Write(string line) => Lines.Add(line);
}
