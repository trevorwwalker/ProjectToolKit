// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Git.Objects;

/// <summary>1061-B (MM-1): the four kinds of object a repository holds.</summary>
public enum GitObjectType
{
    Commit = 1,
    Tree = 2,
    Blob = 3,
    Tag = 4,
}

/// <summary>1061-B (MM-1): one object's kind and its whole content (no header), as <c>git cat-file &lt;type&gt;</c> prints it.</summary>
public sealed record GitObject(GitObjectType Type, byte[] Data)
{
    public static string TypeName(GitObjectType type) => type switch
    {
        GitObjectType.Commit => "commit",
        GitObjectType.Tree => "tree",
        GitObjectType.Blob => "blob",
        GitObjectType.Tag => "tag",
        _ => throw new GitObjectException($"unknown object type {(int)type}"),
    };

    public static GitObjectType ParseType(string name) => name switch
    {
        "commit" => GitObjectType.Commit,
        "tree" => GitObjectType.Tree,
        "blob" => GitObjectType.Blob,
        "tag" => GitObjectType.Tag,
        _ => throw new GitObjectException($"unknown object type '{name}'"),
    };
}

/// <summary>
/// 1061-B (MM-2): a read that failed: an object missing, a corrupt pack or loose file, a pack deleted under the read.
/// It fails that read only; the caller keeps what it had and tries again later.
/// </summary>
public sealed class GitObjectException : Exception
{
    public GitObjectException(string message)
        : base(message)
    {
    }

    public GitObjectException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
