using Microsoft.IdentityModel.JsonWebTokens;

namespace Framework.Api.Core.Security.Jwt;

public static class JwtTokenHelper
{
    public static readonly JsonWebTokenHandler TokenHandler = _CreateHandler();

    private static JsonWebTokenHandler _CreateHandler()
    {
        JsonWebTokenHandler.DefaultMapInboundClaims = false;
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false, };
    }
}
