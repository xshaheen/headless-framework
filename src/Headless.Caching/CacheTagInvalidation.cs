// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Shared read-time predicate for Family-2 logical tag-version invalidation. An entry is invalidated when its
/// birth time (<c>CreatedAt</c>) predates the newest applicable invalidation marker — the max of the global
/// clear-generation marker and every per-tag marker the entry carries. Each provider owns its own marker store
/// and computes the newest marker; this helper holds the single comparison so all tiers agree.
/// </summary>
internal static class CacheTagInvalidation
{
    /// <summary>
    /// Returns whether an entry born at <paramref name="createdAt"/> is invalidated by the newest applicable
    /// marker (<paramref name="newestMarker"/>). A <see langword="null"/> <paramref name="newestMarker"/> means
    /// no marker applies, so the entry is never invalidated. A <see langword="null"/> <paramref name="createdAt"/>
    /// (legacy/pre-CreatedAt entries, or a direct upsert that did not stamp a birth time) biases to recompute:
    /// it is treated as invalidated whenever any marker applies.
    /// </summary>
    /// <param name="createdAt">The entry's birth time (UTC), or <see langword="null"/> when unknown.</param>
    /// <param name="newestMarker">The newest applicable invalidation marker (UTC), or <see langword="null"/> when none applies.</param>
    public static bool IsInvalidated(DateTime? createdAt, DateTime? newestMarker)
    {
        return newestMarker.HasValue && (createdAt is null || newestMarker.Value > createdAt.Value);
    }
}
