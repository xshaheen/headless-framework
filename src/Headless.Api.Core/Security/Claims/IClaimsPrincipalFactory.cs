// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Headless.Api.Security.Claims;

/// <summary>
/// Creates authenticated <see cref="ClaimsPrincipal"/> and <see cref="ClaimsIdentity"/> instances
/// with the authentication type and claim types configured by <see cref="IdentityOptions"/>.
/// </summary>
[PublicAPI]
public interface IClaimsPrincipalFactory
{
    /// <summary>Creates an authenticated <see cref="ClaimsPrincipal"/> wrapping the given claims.</summary>
    /// <param name="claims">One or more claim sequences to include in the identity.</param>
    /// <returns>A <see cref="ClaimsPrincipal"/> whose single identity contains all provided claims.</returns>
    ClaimsPrincipal CreateClaimsPrincipal(params IEnumerable<Claim> claims);

    /// <summary>Creates an authenticated <see cref="ClaimsIdentity"/> with the given claims.</summary>
    /// <param name="claims">One or more claim sequences to include in the identity.</param>
    /// <returns>
    /// A <see cref="ClaimsIdentity"/> with the framework's <c>IdentityAuthenticationType</c> and
    /// claim type mappings derived from <see cref="IdentityOptions.ClaimsIdentity"/>.
    /// </returns>
    ClaimsIdentity CreateClaimsIdentity(params IEnumerable<Claim> claims);
}

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
