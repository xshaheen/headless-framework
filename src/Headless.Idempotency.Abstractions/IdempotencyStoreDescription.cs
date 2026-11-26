// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>The idempotency store this host runs on and the defaults its admissions use.</summary>
/// <param name="Provider">The provider package, for example <c>Headless.Idempotency.PostgreSql</c>.</param>
/// <param name="Location">
/// Where the records live: the quoted, qualified table name the provider's SQL uses on a relational provider (SQLite
/// folds the schema into the table name), the key prefix on the cache provider;
/// <see langword="null" /> for the in-memory provider.
/// </param>
/// <param name="DefaultRetention">How long a completed record replays when the caller passes no retention.</param>
/// <param name="DefaultLeaseDuration">How long an admitted attempt owns its key when the caller passes no lease.</param>
/// <param name="MinimumLeaseDuration">The shortest lease an admission or renewal accepts.</param>
/// <param name="MaximumLeaseDuration">The longest lease an admission or renewal accepts.</param>
/// <param name="PurgeInterval">
/// How often the retention purge runs; <see langword="null" /> when it is disabled, or when the provider's records
/// expire on their own and the purge has nothing to delete (the cache provider).
/// </param>
/// <param name="PurgeBatchSize">The most records one purge statement deletes.</param>
[PublicAPI]
public sealed record IdempotencyStoreDescription(
    string Provider,
    string? Location,
    TimeSpan DefaultRetention,
    TimeSpan DefaultLeaseDuration,
    TimeSpan MinimumLeaseDuration,
    TimeSpan MaximumLeaseDuration,
    TimeSpan? PurgeInterval,
    int PurgeBatchSize
);
