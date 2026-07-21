// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Api.Security.Claims;
using Headless.Checks;
using Headless.Constants;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Headless.Api.Security.Jwt;

/// <summary>Factory for creating and validating signed (and optionally encrypted) JWT tokens.</summary>
[PublicAPI]
public interface IJwtTokenFactory
{
    /// <summary>Creates a signed JWT token from a flat collection of claims.</summary>
    /// <param name="claims">Claims to embed in the token.</param>
    /// <param name="request">Token lifetime, signing/encryption keys, and registered-claim values.</param>
    /// <returns>A compact-serialized JWT string.</returns>
    /// <exception cref="ArgumentException"><see cref="JwtTokenRequest.SigningKey"/> encodes to fewer than 32 bytes.</exception>
    string CreateJwtToken(IEnumerable<Claim> claims, JwtTokenRequest request);

    /// <summary>Creates a signed JWT token from a pre-built <see cref="ClaimsIdentity"/>.</summary>
    /// <param name="identity">Identity whose claims are embedded in the token.</param>
    /// <param name="request">Token lifetime, signing/encryption keys, and registered-claim values.</param>
    /// <returns>A compact-serialized JWT string.</returns>
    /// <exception cref="ArgumentException"><see cref="JwtTokenRequest.SigningKey"/> encodes to fewer than 32 bytes.</exception>
    string CreateJwtToken(ClaimsIdentity identity, JwtTokenRequest request);

    /// <summary>Validates a JWT token and returns the resulting <see cref="ClaimsPrincipal"/>.</summary>
    /// <param name="request">The token, key material, expected issuer and audience, and validation switches.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// The validated <see cref="ClaimsPrincipal"/> on success, or <see langword="null"/> when validation fails
    /// (expired token, bad signature, wrong issuer/audience, etc.).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before validation began.</exception>
    Task<ClaimsPrincipal?> ParseJwtTokenAsync(
        JwtTokenValidationRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Default <see cref="IJwtTokenFactory"/> implementation that uses HMAC-SHA256 signing and
/// optional AES-256-CBC/HMAC-SHA512 encryption via <see cref="JsonWebTokenHandler"/>.
/// </summary>
[PublicAPI]
public sealed class JwtTokenFactory(IClaimsPrincipalFactory claimsPrincipalFactory, TimeProvider timeProvider)
    : IJwtTokenFactory
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><see cref="JwtTokenRequest.SigningKey"/> encodes to fewer than 32 bytes.</exception>
    public string CreateJwtToken(IEnumerable<Claim> claims, JwtTokenRequest request)
    {
        var identity = claimsPrincipalFactory.CreateClaimsIdentity(claims);

        return CreateJwtToken(identity, request);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><see cref="JwtTokenRequest.SigningKey"/> encodes to fewer than 32 bytes.</exception>
    public string CreateJwtToken(ClaimsIdentity identity, JwtTokenRequest request)
    {
        Argument.IsNotNull(request);

        var issuedAt = timeProvider.GetUtcNow().UtcDateTime;

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            IssuedAt = issuedAt,
            Expires = issuedAt.Add(request.Ttl),
            NotBefore = request.NotBefore.HasValue ? issuedAt.Add(request.NotBefore.Value) : null,
            Issuer = request.Issuer,
            Audience = request.Audience,
            SigningCredentials = _GetSigningCredentials(request.SigningKey),
            EncryptingCredentials = request.EncryptingKey is null
                ? null
                : _GetEncryptingCredentials(request.EncryptingKey),
        };

        var token = JwtTokenHelper.TokenHandler.CreateToken(tokenDescriptor);

        return token;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before validation began.</exception>
    public async Task<ClaimsPrincipal?> ParseJwtTokenAsync(
        JwtTokenValidationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = request.ValidateIssuer,
            ValidateAudience = request.ValidateAudience,
            ValidIssuer = request.Issuer,
            ValidAudience = request.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = UserClaimTypes.UserName,
            RoleClaimType = UserClaimTypes.Roles,
            AuthenticationType = AuthenticationConstants.IdentityAuthenticationType,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKey = _CreateSecurityKey(request.SigningKey),
            TokenDecryptionKey = request.EncryptingKey is null ? null : _CreateSecurityKey(request.EncryptingKey),
        };

        cancellationToken.ThrowIfCancellationRequested();

        var result = await JwtTokenHelper
            .TokenHandler.ValidateTokenAsync(request.Token, tokenValidationParameters)
            .ConfigureAwait(false);

        return result.IsValid ? new(result.ClaimsIdentity) : null;
    }

    #region Helper Methods

    private static SigningCredentials _GetSigningCredentials(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);

        if (keyBytes.Length < 32)
        {
            throw new ArgumentException(
                "JWT signing key must be at least 256 bits (32 bytes) for HMAC-SHA256.",
                nameof(key)
            );
        }

        return new(new SymmetricSecurityKey(keyBytes), algorithm: SecurityAlgorithms.HmacSha256);
    }

    private static EncryptingCredentials _GetEncryptingCredentials(string key)
    {
        return new(_CreateSecurityKey(key), JwtConstants.DirectKeyUseAlg, SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    private static SymmetricSecurityKey _CreateSecurityKey(string key)
    {
        return new(Encoding.UTF8.GetBytes(key));
    }

    #endregion
}
