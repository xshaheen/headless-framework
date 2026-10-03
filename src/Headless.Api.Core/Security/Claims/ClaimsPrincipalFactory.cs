// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Headless.Api.Security.Claims;

/// <summary>
/// Default <see cref="IClaimsPrincipalFactory"/> that reads name and role claim types from
/// <see cref="IdentityOptions.ClaimsIdentity"/>.
/// </summary>
[PublicAPI]
public sealed class ClaimsPrincipalFactory(IOptions<IdentityOptions> optionsAccessor) : IClaimsPrincipalFactory
{
    private readonly IdentityOptions _options = optionsAccessor.Value;

    /// <inheritdoc/>
    public ClaimsPrincipal CreateClaimsPrincipal(params IEnumerable<Claim> claims)
    {
        var id = CreateClaimsIdentity(claims);

        return new ClaimsPrincipal(id);
    }

    /// <inheritdoc/>
    public ClaimsIdentity CreateClaimsIdentity(params IEnumerable<Claim> claims)
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
