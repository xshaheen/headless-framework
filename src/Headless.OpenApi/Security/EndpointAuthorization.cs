// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace Headless.OpenApi;

/// <summary>Reads what an endpoint's authorization metadata says about the 401 and 403 it can return.</summary>
/// <remarks>
/// Only metadata is visible here. A global fallback policy applies at run time but appears on no endpoint, so an
/// endpoint protected only by it is documented as anonymous.
/// </remarks>
internal static class EndpointAuthorization
{
    /// <summary>Whether the endpoint carries <see cref="IAllowAnonymous"/>, which bypasses every policy.</summary>
    public static bool AllowsAnonymous(ApiDescription description)
    {
        return description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
    }

    /// <summary>
    /// Whether the endpoint requires an authenticated user: it does not allow anonymous access, and it carries
    /// authorization data, an authorization policy, or an MVC authorization filter that denies anonymous users.
    /// </summary>
    public static bool RequiresAuthentication(ApiDescription description)
    {
        if (AllowsAnonymous(description))
        {
            return false;
        }

        var metadata = description.ActionDescriptor.EndpointMetadata;

        return metadata.OfType<IAuthorizeData>().Any()
            || metadata.OfType<AuthorizationPolicy>().Any()
            || _GetRequirements(description).OfType<DenyAnonymousAuthorizationRequirement>().Any();
    }

    /// <summary>
    /// Whether an authenticated user can still be refused: the endpoint names a policy or roles, or one of its
    /// policies holds a claims, name, operation, roles, or assertion requirement.
    /// </summary>
    /// <remarks>A plain <c>[Authorize]</c> only checks authentication, so it can produce 401 but not 403.</remarks>
    public static bool CanForbid(ApiDescription description)
    {
        if (AllowsAnonymous(description))
        {
            return false;
        }

        var metadata = description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAuthorizeData>().Any(data => data.Policy is not null || data.Roles is not null))
        {
            return true;
        }

        return _GetRequirements(description)
            .Any(requirement =>
                requirement
                    is ClaimsAuthorizationRequirement
                        or NameAuthorizationRequirement
                        or OperationAuthorizationRequirement
                        or RolesAuthorizationRequirement
                        or AssertionRequirement
            );
    }

    /// <summary>The roles named by the endpoint's authorization data, offered as security requirement scopes.</summary>
    public static List<string> GetRoles(ApiDescription description)
    {
        return
        [
            .. description
                .ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>()
                .Where(data => data.Roles is not null)
                .SelectMany(data =>
                    data.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                )
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<IAuthorizationRequirement> _GetRequirements(ApiDescription description)
    {
        var actionDescriptor = description.ActionDescriptor;
        var policies = actionDescriptor.EndpointMetadata.OfType<AuthorizationPolicy>().SelectMany(p => p.Requirements);

        return actionDescriptor.FilterDescriptors.GetPolicyRequirements().Concat(policies);
    }
}
