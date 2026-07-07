// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Security.Jwt;

/// <summary>
/// Parameters for creating a signed (and optionally encrypted) JWT token via
/// <see cref="IJwtTokenFactory.CreateJwtToken(System.Collections.Generic.IEnumerable{System.Security.Claims.Claim}, JwtTokenRequest)"/>.
/// Groups the token's lifetime, keys, and registered-claim values so that the adjacent same-typed
/// values (signing key, encryption key, issuer, audience) cannot be transposed at the call site.
/// </summary>
[PublicAPI]
public sealed record JwtTokenRequest
{
    /// <summary>Token lifetime measured from the current UTC time.</summary>
    public required TimeSpan Ttl { get; init; }

    /// <summary>HMAC-SHA256 signing key (UTF-8 encoded); must be at least 32 bytes.</summary>
    public required string SigningKey { get; init; }

    /// <summary>
    /// Optional AES-256-CBC/HMAC-SHA512 encryption key (UTF-8 encoded).
    /// When <see langword="null"/> the token is signed but not encrypted.
    /// </summary>
    public string? EncryptingKey { get; init; }

    /// <summary>Optional <c>iss</c> claim value.</summary>
    public string? Issuer { get; init; }

    /// <summary>Optional <c>aud</c> claim value.</summary>
    public string? Audience { get; init; }

    /// <summary>
    /// Optional offset from <c>iat</c> at which the token becomes valid.
    /// When <see langword="null"/> no <c>nbf</c> claim is added.
    /// </summary>
    public TimeSpan? NotBefore { get; init; }
}
