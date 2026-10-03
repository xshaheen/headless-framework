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
