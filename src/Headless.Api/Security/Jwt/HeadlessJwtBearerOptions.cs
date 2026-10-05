// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.Security;

/// <summary>
/// The key material and expected claims a JWT bearer scheme registered by <c>AddHeadlessJwtBearer</c> validates
/// against. Set them to the values the host passes to <see cref="IJwtTokenFactory"/> in <see cref="JwtTokenRequest"/>.
/// </summary>
[PublicAPI]
public sealed class HeadlessJwtBearerOptions
{
    /// <summary>
    /// The HMAC-SHA256 key that verifies the token signature, UTF-8 encoded. Must be at least 32 bytes, the same
    /// minimum <see cref="IJwtTokenFactory"/> enforces when it signs.
    /// </summary>
    public string SigningKey { get; set; } = "";

    /// <summary>
    /// The key that decrypts tokens the factory encrypted, UTF-8 encoded. Leave unset when tokens are signed only.
    /// </summary>
    public string? EncryptingKey { get; set; }

    /// <summary>The expected <c>iss</c> claim. Required when <see cref="ValidateIssuer"/> is <see langword="true"/>.</summary>
    public string? Issuer { get; set; }

    /// <summary>The expected <c>aud</c> claim. Required when <see cref="ValidateAudience"/> is <see langword="true"/>.</summary>
    public string? Audience { get; set; }

    /// <summary>Whether the issuer is validated. The default is <see langword="true"/>.</summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>Whether the audience is validated. The default is <see langword="true"/>.</summary>
    public bool ValidateAudience { get; set; } = true;
}

internal sealed class HeadlessJwtBearerOptionsValidator : AbstractValidator<HeadlessJwtBearerOptions>
{
    public HeadlessJwtBearerOptionsValidator()
    {
        RuleFor(x => x.SigningKey)
            .Must(key => key is not null && Encoding.UTF8.GetByteCount(key) >= JwtTokenFactory.MinimumSigningKeyBytes)
            .WithMessage(
                $"SigningKey must be at least {JwtTokenFactory.MinimumSigningKeyBytes} bytes (256 bits) when UTF-8 encoded."
            );
        RuleFor(x => x.EncryptingKey).NotEmpty().When(x => x.EncryptingKey is not null);
        RuleFor(x => x.Issuer).NotEmpty().When(x => x.ValidateIssuer);
        RuleFor(x => x.Audience).NotEmpty().When(x => x.ValidateAudience);
    }
}
