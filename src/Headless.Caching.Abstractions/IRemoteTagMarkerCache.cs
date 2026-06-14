// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Optional capability for a remote (L2) cache that keeps a process-local copy of Family-2 tag/clear invalidation
/// markers (so a tagged read does not pay a round-trip per read). A multi-tier host that learns of an invalidation
/// out-of-band — e.g. <see cref="IInMemoryCache"/>-fronted hybrid receiving a backplane notification — can push the
/// marker timestamp here so the local copy is updated immediately, instead of waiting for the next lazy refresh
/// window. This mirrors FusionCache's backplane optimization where the notification carries the marker payload so
/// peers update their local view without a follow-up L2 read.
/// </summary>
/// <remarks>
/// Implementations must apply the seed as <em>raise-only</em>: a pushed timestamp must never lower a marker the
/// node already knows to be newer. The pushed value is treated as freshly observed (it resets the refresh window).
/// Providers that read markers in-process (no remote round-trip, no local cache) need not implement this.
/// </remarks>
[PublicAPI]
public interface IRemoteTagMarkerCache
{
    /// <summary>Seeds the local copy of <paramref name="tag"/>'s invalidation marker with a timestamp learned out-of-band.</summary>
    void SeedTagMarker(string tag, DateTimeOffset invalidatedAt);

    /// <summary>Seeds the local copy of the global clear-generation marker with a timestamp learned out-of-band.</summary>
    void SeedClearMarker(DateTimeOffset invalidatedAt);
}
