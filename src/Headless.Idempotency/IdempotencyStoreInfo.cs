// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>What a record store says about itself for <see cref="IIdempotentOperations.Describe" />.</summary>
/// <param name="Provider">The provider package's name.</param>
/// <param name="Location">
/// Where the records live (the schema-qualified table, or the cache key prefix); <see langword="null" /> when the
/// provider keeps them in process memory.
/// </param>
/// <param name="PurgeDeletesRecords">
/// Whether <see cref="IIdempotencyRecordStore.PurgeAsync" /> deletes anything. A store whose entries expire on their
/// own answers <see langword="false" />, so the description reports no purge schedule for it.
/// </param>
[PublicAPI]
public sealed record IdempotencyStoreInfo(string Provider, string? Location, bool PurgeDeletesRecords);
