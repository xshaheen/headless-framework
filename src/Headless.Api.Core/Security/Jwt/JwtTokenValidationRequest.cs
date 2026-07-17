// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Security.Jwt;

/// <summary>Specifies the token, keys, and expected claims used to validate a JWT.</summary>
[PublicAPI]
public sealed class JwtTokenValidationRequest
{
    /// <summary>Creates a JWT validation request.</summary>
    /// <param name="token">The compact-serialized JWT string to validate.</param>
    /// <param name="signingKey">The HMAC-SHA256 key used to verify the signature.</param>
    /// <param name="issuer">The expected issuer.</param>
    /// <param name="audience">The expected audience.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public JwtTokenValidationRequest(string token, string signingKey, string issuer, string audience)
    {
        Argument.IsNotNull(token);
        Argument.IsNotNull(signingKey);
        Argument.IsNotNull(issuer);
        Argument.IsNotNull(audience);

        Token = token;
        SigningKey = signingKey;
        Issuer = issuer;
        Audience = audience;
    }

    /// <summary>Gets the compact-serialized JWT string to validate.</summary>
    public string Token { get; }

    /// <summary>Gets the HMAC-SHA256 key used to verify the signature.</summary>
    public string SigningKey { get; }

    /// <summary>Gets the expected issuer.</summary>
    public string Issuer { get; }

    /// <summary>Gets the expected audience.</summary>
    public string Audience { get; }

    /// <summary>Gets the optional JWE decryption key.</summary>
    public string? EncryptingKey { get; init; }

    /// <summary>Gets whether the issuer is validated. The default is <see langword="true"/>.</summary>
    public bool ValidateIssuer { get; init; } = true;

    /// <summary>Gets whether the audience is validated. The default is <see langword="true"/>.</summary>
    public bool ValidateAudience { get; init; } = true;

    /// <inheritdoc/>
    public override string ToString()
    {
        return nameof(JwtTokenValidationRequest);
    }
}
