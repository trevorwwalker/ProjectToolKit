// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Accounting;

/// <summary>
/// One entry of the jobs folder's models.json, as far as accounting needs it (ACCT-2, ACCT-3).
/// Reading and parsing the file from main is Host's; the entries arrive here already read.
/// </summary>
public sealed record ModelEntry
{
    /// <summary>The key it is filed under; what the model column holds.</summary>
    public required string Key { get; init; }

    /// <summary>The entry's <c>name</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>The entry's <c>source</c>: usually a Hugging Face id, whose last part identifies the model.</summary>
    public string Source { get; init; } = "";

    /// <summary>The entry's <c>short</c> name, shown on the page instead of the key.</summary>
    public string Short { get; init; } = "";

    /// <summary>Names the server is known to serve this model under.</summary>
    public IReadOnlyList<string> Served { get; init; } = [];
}
