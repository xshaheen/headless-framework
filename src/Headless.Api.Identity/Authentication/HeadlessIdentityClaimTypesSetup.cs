// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity;

/// <summary>
/// Makes ASP.NET Core Identity write the user id and roles under the claim types <c>ICurrentUser</c> reads
/// (<see cref="UserClaimTypes.UserId"/> and <see cref="UserClaimTypes.Roles"/>), so a principal built by
/// <see cref="SignInManager{TUser}.CreateUserPrincipalAsync"/> carries a user id and roles the framework sees.
/// </summary>
/// <remarks>
/// Only Identity's own defaults are replaced. A claim type the application set explicitly is kept, whether its
/// configuration ran before or after this one.
/// </remarks>
internal sealed class HeadlessIdentityClaimTypesSetup : IConfigureOptions<IdentityOptions>
{
    public void Configure(IdentityOptions options)
    {
        var claims = options.ClaimsIdentity;

        if (string.Equals(claims.UserIdClaimType, ClaimTypes.NameIdentifier, StringComparison.Ordinal))
        {
            claims.UserIdClaimType = UserClaimTypes.UserId;
        }

        if (string.Equals(claims.RoleClaimType, ClaimTypes.Role, StringComparison.Ordinal))
        {
            claims.RoleClaimType = UserClaimTypes.Roles;
        }
    }
}
