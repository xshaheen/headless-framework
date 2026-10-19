// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.OpenApi;

/// <summary>Reads what an endpoint's authorization metadata and policies say about the 401 and 403 it can return.</summary>
/// <remarks>
/// It resolves the policies the authorization middleware would apply: a bare <c>[Authorize]</c> or
/// <c>RequireAuthorization()</c> takes <see cref="AuthorizationOptions.DefaultPolicy"/>, and an endpoint with no
/// authorization data or policy takes <see cref="AuthorizationOptions.FallbackPolicy"/>. Named policies are not
/// resolved: a named policy is assumed able to refuse an authenticated user. A custom
/// <see cref="IAuthorizationPolicyProvider"/> that builds its default or fallback policy outside
/// <see cref="AuthorizationOptions"/> is not seen.
/// </remarks>
internal static class EndpointAuthorization
{
    /// <summary>Whether the endpoint carries <see cref="IAllowAnonymous"/>, which bypasses every policy.</summary>
    public static bool AllowsAnonymous(ApiDescription description)
    {
        return description.GetEndpointMetadata().OfType<IAllowAnonymous>().Any();
    }

    /// <summary>
    /// Whether the endpoint requires an authenticated user: it does not allow anonymous access, and it carries
    /// authorization data, an authorization policy, authorization requirement data, or an MVC authorization filter that
    /// denies anonymous users, or a fallback policy applies to it.
    /// </summary>
    public static bool RequiresAuthentication(ApiDescription description, IServiceProvider services)
    {
        if (AllowsAnonymous(description))
        {
            return false;
        }

        // Any policy refuses an anonymous caller with a challenge, so a fallback policy alone means 401.
        return _HasAuthorizationMetadata(description)
            || description.GetEndpointMetadata().OfType<IAuthorizationRequirementData>().Any()
            || _GetAuthorizationOptions(services).FallbackPolicy is not null
            || _GetFilterRequirements(description).OfType<DenyAnonymousAuthorizationRequirement>().Any();
    }

    /// <summary>
    /// Whether an authenticated user can still be refused: the endpoint names a policy or roles, requires a tenant, or
    /// one of its applied policies holds a claims, name, operation, roles, assertion, or tenant requirement.
    /// </summary>
    /// <remarks>A plain <c>[Authorize]</c> under the default policy only checks authentication, so it can produce 401 but not 403.</remarks>
    public static bool CanForbid(ApiDescription description, IServiceProvider services)
    {
        if (AllowsAnonymous(description))
        {
            return false;
        }

        var metadata = description.GetEndpointMetadata();

        if (metadata.OfType<IAuthorizeData>().Any(data => data.Policy is not null || data.Roles is not null))
        {
            return true;
        }

        return RequiresTenant(description, services)
            || _GetRequirements(description, services)
                .Any(requirement =>
                    requirement
                        is ClaimsAuthorizationRequirement
                            or NameAuthorizationRequirement
                            or OperationAuthorizationRequirement
                            or RolesAuthorizationRequirement
                            or AssertionRequirement
                );
    }

    /// <summary>
    /// Whether a missing tenant can refuse the request with <c>g:tenant_required</c>: the endpoint's effective tenancy
    /// metadata is <see cref="RequireTenantAttribute"/>, or an applied policy holds <see cref="TenantRequirement"/> and
    /// the endpoint does not allow a missing tenant.
    /// </summary>
    public static bool RequiresTenant(ApiDescription description, IServiceProvider services)
    {
        if (AllowsAnonymous(description))
        {
            return false;
        }

        // The later of the two attributes wins, as it does in TenantRequirementHandler.
        var tenancy = description
            .GetEndpointMetadata()
            .LastOrDefault(item => item is RequireTenantAttribute or AllowMissingTenantAttribute);

        return tenancy switch
        {
            RequireTenantAttribute => true,
            AllowMissingTenantAttribute => false,
            _ => _GetRequirements(description, services).OfType<TenantRequirement>().Any(),
        };
    }

    /// <summary>The roles named by the endpoint's authorization data, offered as security requirement scopes.</summary>
    public static List<string> GetRoles(ApiDescription description)
    {
        return
        [
            .. description
                .GetEndpointMetadata()
                .OfType<IAuthorizeData>()
                .Where(data => data.Roles is not null)
                .SelectMany(data =>
                    data.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                )
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static bool _HasAuthorizationMetadata(ApiDescription description)
    {
        var metadata = description.GetEndpointMetadata();

        return metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<AuthorizationPolicy>().Any();
    }

    private static IEnumerable<IAuthorizationRequirement> _GetRequirements(
        ApiDescription description,
        IServiceProvider services
    )
    {
        var metadata = description.GetEndpointMetadata();
        var options = _GetAuthorizationOptions(services);
        var requirements = _GetFilterRequirements(description)
            .Concat(metadata.OfType<AuthorizationPolicy>().SelectMany(policy => policy.Requirements))
            .Concat(metadata.OfType<IAuthorizationRequirementData>().SelectMany(data => data.GetRequirements()));

        // The middleware combines the default policy for every authorization datum that names no policy or roles.
        if (
            metadata
                .OfType<IAuthorizeData>()
                .Any(data => string.IsNullOrWhiteSpace(data.Policy) && string.IsNullOrWhiteSpace(data.Roles))
        )
        {
            requirements = requirements.Concat(options.DefaultPolicy.Requirements);
        }

        if (!_HasAuthorizationMetadata(description) && options.FallbackPolicy is { } fallback)
        {
            requirements = requirements.Concat(fallback.Requirements);
        }

        return requirements;
    }

    private static IEnumerable<IAuthorizationRequirement> _GetFilterRequirements(ApiDescription description)
    {
        return description.ActionDescriptor.FilterDescriptors.GetPolicyRequirements();
    }

    private static AuthorizationOptions _GetAuthorizationOptions(IServiceProvider services)
    {
        return services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
    }
}
