// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// Maximum lengths of the stored idempotency record's text and binary columns. Call validation and every provider's
/// table definition read these, so a value that passes validation always fits its column.
/// </summary>
/// <remarks>
/// The tenant id and key form the record's primary key: 384 characters, 768 bytes as <c>nvarchar</c>, under SQL
/// Server's 900-byte clustered-key limit.
/// </remarks>
[PublicAPI]
public static class IdempotencyFieldLimits
{
    /// <summary>The maximum length of a tenant id.</summary>
    public const int TenantIdMaxLength = 128;

    /// <summary>The maximum length of an idempotency key.</summary>
    public const int KeyMaxLength = 256;

    /// <summary>The maximum length of a result contract tag.</summary>
    public const int ContractMaxLength = 256;

    /// <summary>The maximum length of a fingerprint algorithm tag.</summary>
    public const int FingerprintAlgorithmMaxLength = 32;

    /// <summary>The maximum length, in bytes, of a fingerprint digest.</summary>
    public const int FingerprintMaxLength = 64;
}
