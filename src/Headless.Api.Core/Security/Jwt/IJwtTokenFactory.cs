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
