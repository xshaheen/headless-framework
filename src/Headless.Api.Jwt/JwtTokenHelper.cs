// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.IdentityModel.JsonWebTokens;

namespace Headless.Api.Security;

/// <summary>Shared <see cref="JsonWebTokenHandler"/> instance used by <see cref="JwtTokenFactory"/>.</summary>
public static class JwtTokenHelper
{
    internal static readonly JsonWebTokenHandler TokenHandler = _CreateHandler();

    private static JsonWebTokenHandler _CreateHandler()
    {
        // Inbound claim mapping is disabled on this instance rather than through the JsonWebTokenHandler statics,
        // so the host's other token handlers keep their own defaults.
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
