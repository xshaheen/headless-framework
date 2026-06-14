// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Thrown by <see cref="RedisCache.RemoveByTagAsync"/> when the per-call iteration budget is exhausted before
/// the tag hash is fully drained — typically because concurrent writes keep re-adding members to the tag faster
/// than the sweep can remove them. The members removed so far are gone; the operation is incomplete. Retrying
/// completes the invalidation once the write pressure subsides.
/// </summary>
[PublicAPI]
public sealed class CacheTagRemovalIncompleteException : Exception
{
    /// <summary>The tag whose removal did not complete.</summary>
    public string? Tag { get; }

    /// <summary>The number of tag members removed before the budget was exhausted.</summary>
    public int Removed { get; }

    /// <summary>The number of tag members still present (a best-effort probe taken after the sweep stopped).</summary>
    public int Remaining { get; }

    public CacheTagRemovalIncompleteException() { }

    public CacheTagRemovalIncompleteException(string? message)
        : base(message) { }

    public CacheTagRemovalIncompleteException(string? message, Exception? innerException)
        : base(message, innerException) { }

    public CacheTagRemovalIncompleteException(string tag, int removed, int remaining)
        : base(
            $"Tag '{tag}' removal stopped after removing {removed} members with {remaining} remaining: the "
                + "iteration budget was exhausted under concurrent writes. Retry to complete invalidation."
        )
    {
        Tag = tag;
        Removed = removed;
        Remaining = remaining;
    }
}
