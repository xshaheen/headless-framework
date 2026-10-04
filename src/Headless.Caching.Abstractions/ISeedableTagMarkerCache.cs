// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Defines capabilities for caches that keep a process-local copy of invalidation markers.
/// Exposes seeding methods for process-local updates and durable write methods for shared stores.
/// </summary>
/// <remarks>
/// All marker operations are raise-only. Pushed or written timestamps cannot lower an existing newer marker.
/// </remarks>
[PublicAPI]
public interface ISeedableTagMarkerCache
{
    /// <summary>Seeds the local copy of an invalidation marker for a tag with a timestamp learned out-of-band.</summary>
    void SeedTagMarker(string tag, DateTimeOffset invalidatedAt);

    /// <summary>Seeds the local copy of the global clear generation marker with a timestamp learned out-of-band.</summary>
    void SeedClearMarker(DateTimeOffset invalidatedAt);

    /// <summary>
    /// Seeds the local copy of the global remove generation marker with a timestamp learned out-of-band.
    /// </summary>
    void SeedRemoveMarker(DateTimeOffset invalidatedAt);

    /// <summary>
    /// Writes an invalidation marker for a tag to the durable store at <paramref name="invalidatedAt"/> and updates the local copy.
    /// The durable write is raise-only and cannot overwrite a newer marker.
    /// </summary>
    /// <param name="tag">The invalidation tag.</param>
    /// <param name="invalidatedAt">The invalidation timestamp to write.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    ValueTask WriteTagMarkerAsync(
        string tag,
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    );

    /// <summary>Writes the global clear generation marker to the durable store using a raise-only update.</summary>
    ValueTask WriteClearMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default);

    /// <summary>Writes the global remove generation marker to the durable store using a raise-only update.</summary>
    ValueTask WriteRemoveMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default);
}
