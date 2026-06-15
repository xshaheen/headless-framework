// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Optional capability for a cache that keeps a process-local copy of Family-2 tag/clear/remove invalidation
/// markers. It exposes two families: <c>Seed*</c> update only the <em>process-local</em> copy from knowledge gained
/// out-of-band (e.g. a backplane notification carrying the originator timestamp), so the local view updates
/// immediately instead of waiting for the next lazy refresh window; <c>Write*</c> write the marker to the
/// <em>durable shared store</em> (then update the local copy), used by a multi-tier host for the live invalidation
/// and for auto-recovery replay after an L2 outage. Mirrors FusionCache's payload-carrying-backplane optimization.
/// </summary>
/// <remarks>
/// All operations are <em>raise-only</em>: a pushed or written timestamp must never lower a marker the store
/// already knows to be newer. This is load-bearing for recovery — a replay may carry an <em>older</em> original
/// timestamp than a bump that already landed, and an atomic raise-only durable write (e.g. a set-if-higher Lua
/// script on Redis) is what prevents it from resurrecting entries written after the invalidation. A custom L2 that
/// aliases <c>Write*</c> to <c>Seed*</c> (local-only) silently breaks recovery: the shared-store marker is never
/// written. Any cache holding process-local marker state should implement this so it can receive single-clock
/// seeds — including in-process caches: <c>InMemoryCache</c> implements the tag/clear seeds, and its
/// <see cref="SeedRemoveMarker"/> / <see cref="WriteRemoveMarkerAsync"/> are no-ops (its <c>FlushAsync</c> wipes
/// physically, so it has no logical remove-generation marker).
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

    /// <summary>
    /// Writes <paramref name="tag"/>'s invalidation marker to the <em>durable</em> store at
    /// <paramref name="invalidatedAt"/> (then updates the local copy), as opposed to <see cref="SeedTagMarker"/>
    /// which only updates the local copy. The durable write is <em>raise-only</em> — it must never lower a newer
    /// marker already stored — so a multi-tier host can carry the original invalidation timestamp here (e.g. on a
    /// live invalidation or an auto-recovery replay after an outage) without resurrecting entries written after it.
    /// </summary>
    /// <param name="tag">The invalidation tag. Must not be null or empty; implementations validate (e.g. <c>Argument.IsNotNullOrEmpty</c>).</param>
    /// <param name="invalidatedAt">The original invalidation timestamp to write (raise-only).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask WriteTagMarkerAsync(
        string tag,
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    );

    /// <summary>Durable, raise-only write of the global clear-generation marker at <paramref name="invalidatedAt"/>. See <see cref="WriteTagMarkerAsync"/>.</summary>
    ValueTask WriteClearMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default);

    /// <summary>Durable, raise-only write of the global remove-generation marker at <paramref name="invalidatedAt"/>. See <see cref="WriteTagMarkerAsync"/>. A cache whose <c>FlushAsync</c> wipes physically implements this as a no-op.</summary>
    ValueTask WriteRemoveMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default);
}
