using System.Security.Claims;
using System.Text;
using Framework.BuildingBlocks.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Framework.Api.Core.Security.Jwt;

public interface IJwtTokenFactory
{
    string CreateJwtToken(
        ClaimsIdentity identity,
        TimeSpan ttl,
        string signingKey,
        string? encryptingKey,
        string? issuer,
        string? audience,
        TimeSpan? notBefore = null
    );
}

public sealed class JwtTokenFactory(IClock clock) : IJwtTokenFactory
{
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
        var issuedAt = clock.Now.DateTime;

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

    #region Helper Methods

    private static SigningCredentials _GetSigningCredentials(string key)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

        return new(securityKey, algorithm: SecurityAlgorithms.HmacSha256);
    }

    private static EncryptingCredentials _GetEncryptingCredentials(string key)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

        return new(securityKey, JwtConstants.DirectKeyUseAlg, SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    #endregion
}
