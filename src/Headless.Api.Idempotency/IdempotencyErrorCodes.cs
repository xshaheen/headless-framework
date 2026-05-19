// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>
/// Stable machine-readable error codes emitted by the idempotency middleware in
/// <see cref="Headless.Primitives.ErrorDescriptor"/>.<c>Code</c>. Clients should branch on
/// these constants rather than inspect the human-readable description, which is localized.
/// </summary>
[PublicAPI]
public static class IdempotencyErrorCodes
{
    /// <summary>Same idempotency key reused with a different request body. Maps to 409 or 422 (configurable).</summary>
    public const string KeyReused = "g:idempotency_key_reused";

    /// <summary>An identical request with this idempotency key is still being processed. Maps to 409.</summary>
    public const string InFlight = "g:idempotency_in_flight";

    /// <summary>Timed out waiting for an in-flight request with this idempotency key to complete. Maps to 409.</summary>
    public const string InFlightTimeout = "g:idempotency_in_flight_timeout";

    /// <summary>Request body exceeds the configured limit for idempotency processing. Maps to 413.</summary>
    public const string BodyTooLarge = "g:idempotency_body_too_large";

    /// <summary>
    /// Idempotency-Key header value is malformed (length over 255, contains control characters,
    /// or multi-valued). Maps to 400.
    /// </summary>
    public const string KeyMalformed = "g:idempotency_key_malformed";
}
