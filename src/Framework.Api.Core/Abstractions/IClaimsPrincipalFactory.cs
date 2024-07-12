using System.Security.Claims;
using Framework.BuildingBlocks.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Framework.Api.Core.Abstractions;

public interface IClaimsPrincipalFactory
{
    ClaimsPrincipal CreateClaimsPrincipal(IEnumerable<Claim> claims);

    ClaimsIdentity CreateClaimsIdentity(IEnumerable<Claim> claims);
}

public sealed class ClaimsPrincipalFactory(IOptions<IdentityOptions> options) : IClaimsPrincipalFactory
{
    private readonly IdentityOptions _options = options.Value;

    public ClaimsPrincipal CreateClaimsPrincipal(IEnumerable<Claim> claims)
    {
        var id = CreateClaimsIdentity(claims);

        return new ClaimsPrincipal(id);
    }

    public ClaimsIdentity CreateClaimsIdentity(IEnumerable<Claim> claims)
    {
        var id = new ClaimsIdentity(
            claims: claims,
            authenticationType: AuthenticationConstants.IdentityAuthenticationType,
            nameType: _options.ClaimsIdentity.UserNameClaimType,
            roleType: _options.ClaimsIdentity.RoleClaimType
        );

        return id;
    }
}
