// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Requirements;

/// <summary>
/// Authorization policy provider that resolves a defined permission name as a policy, so
/// <c>[Authorize("Orders.Edit")]</c> and <c>RequireAuthorization("Orders.Edit")</c> work without an
/// <c>AddPolicy</c> call per permission. Policies registered through <see cref="AuthorizationOptions"/> are asked
/// first and always win; on a miss, a name that is a defined permission (enabled or disabled) resolves to a policy
/// whose only requirement is <see cref="PermissionRequirement"/>. Any other name resolves to <see langword="null"/>,
/// leaving ASP.NET Core's "policy not found" error unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddHeadlessPermissions</c> registers this provider in place of ASP.NET Core's default provider. A host that
/// registers its own <see cref="IAuthorizationPolicyProvider"/> first keeps it; such a host can construct this type
/// with <c>ActivatorUtilities</c> and consult it on its own misses.
/// </para>
/// <para>
/// Permission names match ordinally, while ASP.NET Core matches registered policy names ignoring case. Resolved
/// policies are cached for the provider's lifetime; misses are not, because the dynamic definition store can define
/// a permission at runtime. A failure while reading definitions propagates rather than reporting the name as unknown.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _inner;
    private readonly IPermissionDefinitionManager _definitionManager;
    private readonly string? _prefix;
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public PermissionPolicyProvider(
        IOptions<AuthorizationOptions> authorizationOptions,
        IPermissionDefinitionManager definitionManager,
        IOptions<PermissionManagementOptions> managementOptions
    )
    {
        Argument.IsNotNull(authorizationOptions);
        Argument.IsNotNull(managementOptions);

        _inner = new DefaultAuthorizationPolicyProvider(authorizationOptions);
        _definitionManager = Argument.IsNotNull(definitionManager);
        _prefix = managementOptions.Value.PolicyNamePrefix;
    }

    /// <summary>
    /// Always <see langword="true"/>: a name maps to the same policy for the provider's lifetime, so the
    /// authorization middleware may cache the combined policy per endpoint.
    /// </summary>
    public bool AllowsCachingPolicies => true;

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return _inner.GetDefaultPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _inner.GetFallbackPolicyAsync();
    }

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        Argument.IsNotNull(policyName);

        var policy = await _inner.GetPolicyAsync(policyName).ConfigureAwait(false);

        if (policy is not null)
        {
            return policy;
        }

        if (_policies.TryGetValue(policyName, out policy))
        {
            return policy;
        }

        var permissionName = _GetPermissionName(policyName);

        if (permissionName is null)
        {
            return null;
        }

        // The provider contract carries no token; a lookup failure propagates so an outage never reads as an
        // unknown policy, and nothing is cached for it.
        var definition = await _definitionManager
            .FindAsync(permissionName, CancellationToken.None)
            .ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        policy = new AuthorizationPolicyBuilder().AddRequirements(new PermissionRequirement(definition.Name)).Build();

        return _policies.GetOrAdd(policyName, policy);
    }

    private string? _GetPermissionName(string policyName)
    {
        if (_prefix is null)
        {
            return policyName;
        }

        if (!policyName.StartsWith(_prefix, StringComparison.Ordinal) || policyName.Length == _prefix.Length)
        {
            return null;
        }

        return policyName[_prefix.Length..];
    }
}
