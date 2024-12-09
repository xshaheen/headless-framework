// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Api.Security.Claims;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Framework.Api.Security.Jwt;

public interface IJwtTokenFactory
{
    string CreateJwtToken(
        IEnumerable<Claim> claims,
        TimeSpan ttl,
        string signingKey,
        string? encryptingKey,
        string? issuer,
        string? audience,
        TimeSpan? notBefore = null
    );

    string CreateJwtToken(
        ClaimsIdentity identity,
        TimeSpan ttl,
        string signingKey,
        string? encryptingKey,
        string? issuer,
        string? audience,
        TimeSpan? notBefore = null
    );

    Task<ClaimsPrincipal?> ParseJwtTokenAsync(
        string token,
        string signingKey,
        string? encryptingKey,
        string issuer,
        string audience,
        bool validateIssuer = true,
        bool validateAudience = true
    );
}

public sealed class JwtTokenFactory(IClaimsPrincipalFactory claimsPrincipalFactory, IClock clock) : IJwtTokenFactory
{
    public string CreateJwtToken(
        IEnumerable<Claim> claims,
        TimeSpan ttl,
        string signingKey,
        string? encryptingKey,
        string? issuer,
        string? audience,
        TimeSpan? notBefore = null
    )
    {
        var identity = claimsPrincipalFactory.CreateClaimsIdentity(claims);

        return CreateJwtToken(identity, ttl, signingKey, encryptingKey, issuer, audience, notBefore);
    }

    public string CreateJwtToken(
        ClaimsIdentity identity,
        TimeSpan ttl,
        string signingKey,
        string? encryptingKey,
        string? issuer,
        string? audience,
        TimeSpan? notBefore = null
    )
    {
        var issuedAt = clock.UtcNow.DateTime;

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            IssuedAt = issuedAt,
            Expires = issuedAt.Add(ttl),
            NotBefore = notBefore.HasValue ? issuedAt.Add(notBefore.Value) : null,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = _GetSigningCredentials(signingKey),
            EncryptingCredentials = encryptingKey is null ? null : _GetEncryptingCredentials(encryptingKey),
        };

        var token = JwtTokenHelper.TokenHandler.CreateToken(tokenDescriptor);

        return token;
    }

    public async Task<ClaimsPrincipal?> ParseJwtTokenAsync(
        string token,
        string signingKey,
        string? encryptingKey,
        string issuer,
        string audience,
        bool validateIssuer = true,
        bool validateAudience = true
    )
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = validateIssuer,
            ValidateAudience = validateAudience,
            ValidIssuer = issuer,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = FrameworkClaimTypes.UserName,
            RoleClaimType = FrameworkClaimTypes.Roles,
            AuthenticationType = AuthenticationConstants.IdentityAuthenticationType,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKey = _CreateSecurityKey(signingKey),
            TokenDecryptionKey = encryptingKey is null ? null : _CreateSecurityKey(encryptingKey),
        };

        var result = await JwtTokenHelper.TokenHandler.ValidateTokenAsync(token, tokenValidationParameters);

        return result.IsValid ? new(result.ClaimsIdentity) : null;
    }

    #region Helper Methods

    private static SigningCredentials _GetSigningCredentials(string key)
    {
        return new(_CreateSecurityKey(key), algorithm: SecurityAlgorithms.HmacSha256);
    }

    private static EncryptingCredentials _GetEncryptingCredentials(string key)
    {
        return new(_CreateSecurityKey(key), JwtConstants.DirectKeyUseAlg, SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    private static SymmetricSecurityKey _CreateSecurityKey(string key) => new(Encoding.UTF8.GetBytes(key));

    #endregion
}
