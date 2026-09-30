// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Headless.Api.Idempotency;

/// <summary>
/// The captured response a completed request replays: status, allowlisted headers, and body bytes. Stored in the
/// durable idempotency record as UTF-8 JSON under <see cref="Contract" />.
/// </summary>
internal sealed class IdempotencyResponseSnapshot
{
    /// <summary>
    /// The contract tag the snapshot is stored under. A change to this shape needs a new tag, so a replay refuses bytes
    /// an older version wrote instead of misreading them.
    /// </summary>
    public const string Contract = "headless.api.idempotency.response/v1";

    public int StatusCode { get; init; }

    /// <summary>
    /// Allowlisted response headers captured at completion. Replay filters them through the allowlist, whose lookups
    /// are case-insensitive, so this dictionary's own comparer does not matter.
    /// </summary>
    public Dictionary<string, string[]> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Body { get; init; } = [];
}

[JsonSerializable(typeof(IdempotencyResponseSnapshot))]
internal sealed partial class IdempotencyJsonContext : JsonSerializerContext;
