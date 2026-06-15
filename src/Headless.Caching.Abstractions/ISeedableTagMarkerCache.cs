// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Optional capability for a cache that keeps a process-local copy of Family-2 tag/clear invalidation markers (so
/// a tagged read does not pay a round-trip per read) and can have a marker <em>seeded</em> into that local copy
/// from knowledge gained out-of-band. A multi-tier host that learns of an invalidation via its backplane can push
/// the marker timestamp here so the local copy updates immediately instead of waiting for the next lazy refresh
/// window — mirroring FusionCache's optimization where the notification carries the marker payload so peers update
/// their local view without a follow-up read of the shared store.
/// </summary>
/// <remarks>
/// Implementations must apply the seed as <em>raise-only</em>: a pushed timestamp must never lower a marker the
/// node already knows to be newer. The pushed value is treated as freshly observed (it resets the refresh window).
/// Any cache holding process-local marker state should implement this so it can receive single-clock seeds —
/// including in-process caches: <c>InMemoryCache</c> implements the tag/clear seeds and treats
/// <see cref="SeedRemoveMarker"/> as a no-op (its <c>FlushAsync</c> wipes physically, so it has no logical
/// remove-generation marker to seed).
/// </remarks>
[PublicAPI]
public interface ISeedableTagMarkerCache
{
    /// <summary>Seeds the local copy of <paramref name="tag"/>'s invalidation marker with a timestamp learned out-of-band.</summary>
    void SeedTagMarker(string tag, DateTimeOffset invalidatedAt);

    /// <summary>Seeds the local copy of the global clear-generation marker with a timestamp learned out-of-band.</summary>
    void SeedClearMarker(DateTimeOffset invalidatedAt);

    /// <summary>
    /// Seeds the local copy of the global remove-generation marker with a timestamp learned out-of-band. This is the
    /// logical <c>FlushAsync</c> marker: entries born before it read as a hard miss with <em>no</em> fail-safe
    /// reserve, unlike <see cref="SeedClearMarker"/> (which preserves reserves). Caches whose <c>FlushAsync</c> wipes
    /// physically have no such marker and implement this as a no-op.
    /// </summary>
    void SeedRemoveMarker(DateTimeOffset invalidatedAt);
}
