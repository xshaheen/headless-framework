// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

namespace Headless.Idempotency;

/// <summary>
/// The store this host runs on and the defaults its admissions use, with every duration in whole seconds.
/// </summary>
/// <param name="Location">
/// The qualified table on a relational provider, the key prefix on the cache provider; <see langword="null"/> in memory.
/// </param>
/// <param name="PurgeIntervalSeconds">
/// How often the retention purge runs; <see langword="null"/> when it is off, or when the records expire on their own
/// (the cache provider).
/// </param>
internal sealed record IdempotencyStoreView(
    string Provider,
    string? Location,
    long DefaultRetentionSeconds,
    long DefaultLeaseSeconds,
    long MinimumLeaseSeconds,
    long MaximumLeaseSeconds,
    long? PurgeIntervalSeconds,
    int PurgeBatchSize
)
{
    public static IdempotencyStoreView From(IdempotencyStoreDescription description) =>
        new(
            description.Provider,
            description.Location,
            (long)description.DefaultRetention.TotalSeconds,
            (long)description.DefaultLeaseDuration.TotalSeconds,
            (long)description.MinimumLeaseDuration.TotalSeconds,
            (long)description.MaximumLeaseDuration.TotalSeconds,
            description.PurgeInterval is { } interval ? (long)interval.TotalSeconds : null,
            description.PurgeBatchSize
        );
}

/// <summary>A lookup of one key: the key as the app admitted it, and the tenant it was admitted under.</summary>
/// <remarks><see cref="TenantId"/> absent or <see langword="null"/> reads the host scope, never every tenant.</remarks>
internal sealed record IdempotencyLookupBody
{
    public string? Key { get; init; }

    public string? TenantId { get; init; }
}

/// <summary>The stored request fingerprint, with its digest as lowercase hex.</summary>
internal sealed record IdempotencyFingerprintView(string Algorithm, string Hash);

/// <summary>
/// What the store holds for one key: its state, lease, fingerprint, retention, and the contract and size of its stored
/// result and recovery point. It never carries the stored bytes; a field the provider does not keep is omitted.
/// </summary>
internal sealed record IdempotencyRecordView(
    string Status,
    long? Generation,
    int? TakeoverCount,
    IdempotencyFingerprintView Fingerprint,
    DateTimeOffset? LeaseExpiresAt,
    bool IsLeaseExpired,
    DateTimeOffset RetentionUntil,
    DateTimeOffset ObservedAt,
    string? ResultContract,
    long? ResultSizeBytes,
    string? RecoveryPoint,
    string? RecoveryContract,
    long? RecoveryStateSizeBytes
)
{
    public static IdempotencyRecordView From(IdempotencyRecordSnapshot snapshot) =>
        new(
            snapshot.Status switch
            {
                IdempotencyPeekStatus.Pending => nameof(IdempotencyPeekStatus.Pending),
                IdempotencyPeekStatus.Completed => nameof(IdempotencyPeekStatus.Completed),
                IdempotencyPeekStatus.Released => nameof(IdempotencyPeekStatus.Released),
                _ => nameof(IdempotencyPeekStatus.Absent),
            },
            snapshot.Generation,
            snapshot.TakeoverCount,
            new IdempotencyFingerprintView(
                snapshot.Fingerprint.Algorithm,
                Convert.ToHexStringLower(snapshot.Fingerprint.Hash.Span)
            ),
            snapshot.LeaseExpiresAt,
            snapshot.IsLeaseExpired,
            snapshot.RetentionUntil,
            snapshot.ObservedAt,
            snapshot.ResultContract,
            snapshot.ResultSizeBytes,
            snapshot.RecoveryPoint,
            snapshot.RecoveryContract,
            snapshot.RecoveryStateSizeBytes
        );
}

/// <summary>
/// The answer to a lookup: the tenant it read, whether a record exists, and the record's metadata. It never echoes the
/// key: the screen shows the key from its own form.
/// </summary>
/// <param name="TenantId">The tenant read; <see langword="null"/> for the host scope.</param>
internal sealed record IdempotencyLookupView(string? TenantId, bool Found, IdempotencyRecordView? Record);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IdempotencyStoreView))]
[JsonSerializable(typeof(IdempotencyLookupBody))]
[JsonSerializable(typeof(IdempotencyLookupView))]
internal sealed partial class IdempotencyDashboardJsonContext : JsonSerializerContext;
