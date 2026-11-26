// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>The idempotency store this host runs on and the defaults its admissions use.</summary>
[PublicAPI]
public sealed record IdempotencyStoreDescription
{
    /// <summary>Gets the provider package, for example <c>Headless.Idempotency.PostgreSql</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets where the records live: the quoted, qualified table name the provider's SQL uses on a relational provider
    /// (SQLite folds the schema into the table name), the key prefix on the cache provider; <see langword="null" /> for
    /// the in-memory provider.
    /// </summary>
    public required string? Location { get; init; }

    /// <summary>Gets how long a completed record replays when the caller passes no retention.</summary>
    public required TimeSpan DefaultRetention { get; init; }

    /// <summary>Gets how long an admitted attempt owns its key when the caller passes no lease.</summary>
    public required TimeSpan DefaultLeaseDuration { get; init; }

    /// <summary>Gets the shortest lease an admission or renewal accepts.</summary>
    public required TimeSpan MinimumLeaseDuration { get; init; }

    /// <summary>Gets the longest lease an admission or renewal accepts.</summary>
    public required TimeSpan MaximumLeaseDuration { get; init; }

    /// <summary>
    /// Gets how often the retention purge runs; <see langword="null" /> when it is disabled, or when the provider's
    /// records expire on their own and the purge has nothing to delete (the cache provider).
    /// </summary>
    public required TimeSpan? PurgeInterval { get; init; }

    /// <summary>Gets the most records one purge statement deletes.</summary>
    public required int PurgeBatchSize { get; init; }
}
