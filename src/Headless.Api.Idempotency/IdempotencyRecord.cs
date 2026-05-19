// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

internal enum RecordKind
{
    InFlight = 0,
    Complete = 1,
}

internal sealed class IdempotencyRecord
{
    public RecordKind Kind { get; init; }
    public int StatusCode { get; init; }
    public Dictionary<string, string[]> Headers { get; init; } = [];
    public byte[] Body { get; init; } = [];
    public byte[]? Fingerprint { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
