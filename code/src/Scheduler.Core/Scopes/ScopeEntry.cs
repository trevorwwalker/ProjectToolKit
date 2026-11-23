namespace Scheduler.Core.Scopes;

/// <summary>What a Scope line entry names (SC-1, Q5).</summary>
public enum ScopeEntryKind
{
    /// <summary>One file.</summary>
    File,

    /// <summary>A folder (ends in <c>/</c>) and everything inside it.</summary>
    Folder,

    /// <summary>A glob (<c>*</c>, <c>?</c>, or <c>**</c> across folders): matched by <see cref="PathGlob"/> (961-A).</summary>
    Pattern,
}

/// <summary>One entry of a card's Scope line (SC-1, SC-5).</summary>
/// <param name="Path">The path as written, without the <c>(add-only)</c> mark.</param>
/// <param name="Kind">File, folder or pattern.</param>
/// <param name="AddOnly">Marked <c>(add-only)</c>: other cards may add to it at the same time.</param>
public sealed record ScopeEntry(string Path, ScopeEntryKind Kind, bool AddOnly = false);
