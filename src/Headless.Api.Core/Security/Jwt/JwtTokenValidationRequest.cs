// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Security.Jwt;

/// <summary>Specifies the token, keys, and expected claims used to validate a JWT.</summary>
[PublicAPI]
public sealed class JwtTokenValidationRequest
{
    /// <summary>Gets the compact-serialized JWT string to validate.</summary>
    /// <exception cref="ArgumentNullException">The initialized value is <see langword="null"/>.</exception>
    public required string Token
    {
        get;
        init => field = Argument.IsNotNull(value);
    }

    /// <summary>Gets the HMAC-SHA256 key used to verify the signature.</summary>
    /// <exception cref="ArgumentNullException">The initialized value is <see langword="null"/>.</exception>
    public required string SigningKey
    {
        get;
        init => field = Argument.IsNotNull(value);
    }

    /// <summary>Gets the expected issuer.</summary>
    /// <exception cref="ArgumentNullException">The initialized value is <see langword="null"/>.</exception>
    public required string Issuer
    {
        get;
        init => field = Argument.IsNotNull(value);
    }

    /// <summary>Gets the expected audience.</summary>
    /// <exception cref="ArgumentNullException">The initialized value is <see langword="null"/>.</exception>
    public required string Audience
    {
        get;
        init => field = Argument.IsNotNull(value);
    }

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
