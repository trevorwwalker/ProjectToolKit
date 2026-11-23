namespace Scheduler.Core.Locks;

/// <summary>What the <c>pre-push</c> hook's <c>check-push</c> knows about one pushed ref (design <c>dynamic-locks.md</c>, DL-18).</summary>
public sealed record PushCheckRequest
{
    /// <summary>The pushing card (<c>PTK_CARD</c>), its file name without <c>.md</c>.</summary>
    public required string Card { get; init; }

    /// <summary>The remote ref pushed to, as <c>refs/heads/main</c> or <c>main</c>.</summary>
    public string Ref { get; init; } = "refs/heads/main";

    /// <summary>The main branch's name; only a push to it is checked.</summary>
    public string MainBranch { get; init; } = "main";

    /// <summary>The paths changed in <c>&lt;remote sha&gt;..&lt;local sha&gt;</c>.</summary>
    public IReadOnlyList<PushCheckPath> Paths { get; init; } = [];

    /// <summary>The card's row in the lock table (DL-20), or null when it has none.</summary>
    public CardLocks? Locks { get; init; }

    /// <summary>categories.json's <c>shared.addOnly</c> paths (DL-15 a).</summary>
    public IReadOnlyList<string> AddOnly { get; init; } = [];

    /// <summary>The file whose <c>&lt;Version&gt;</c> line any card may bump (DL-15 a, DL-17).</summary>
    public string VersionFile { get; init; } = "code/src/Directory.Build.props";

    /// <summary>Main's version, or null when unknown.</summary>
    public string? MainVersion { get; init; }

    /// <summary>The pushed version when the range changes the <c>&lt;Version&gt;</c> line, else null.</summary>
    public string? PushedVersion { get; init; }

    /// <summary>The exe's full path, for the lock command a refusal names.</summary>
    public string Exe { get; init; } = "scheduler-next.exe";
}
