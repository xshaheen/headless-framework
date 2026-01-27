// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.IdentityModel.JsonWebTokens;

namespace Framework.Api.Security.Jwt;

public static class JwtTokenHelper
{
    public static readonly JsonWebTokenHandler TokenHandler = _CreateHandler();

    private static JsonWebTokenHandler _CreateHandler()
    {
        // Static defaults (DefaultMapInboundClaims, DefaultInboundClaimTypeMap)
        // are configured once in Setup.ConfigureGlobalSettings().
        return new JsonWebTokenHandler
        {
            MapInboundClaims = false,
            SetDefaultTimesOnTokenCreation = false,
            // Default lifetime of tokens created.
            // When creating tokens, if 'expires', 'notbefore' or 'issuedat' are null, then a default will be set to:
            // - issuedat = DateTime.UtcNow,
            // - notbefore = DateTime.UtcNow,
            // - expires = DateTime.UtcNow + TimeSpan.FromMinutes(TokenLifetimeInMinutes).
            TokenLifetimeInMinutes = 60,
            MaximumTokenSizeInBytes = 256_000,
        };
    }
}
