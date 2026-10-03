// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Requirements;

/// <summary>Default <see cref="IAuthorizationPolicyCatalog"/>.</summary>
internal sealed class AuthorizationPolicyCatalog(
    IOptions<AuthorizationOptions> authorizationOptions,
    IOptions<PermissionManagementOptions> managementOptions,
    IPermissionDefinitionManager definitionManager,
    IAuthorizationPolicyProvider policyProvider
) : IAuthorizationPolicyCatalog
{
    public Task<IReadOnlySet<string>> GetRegisteredPolicyNamesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> names = _GetPolicyMap(authorizationOptions.Value).Keys.ToHashSet(StringComparer.Ordinal);

        return Task.FromResult(names);
    }

    public async Task<IReadOnlySet<string>> GetPermissionNamesAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await definitionManager.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);

        return permissions.Select(permission => permission.Name).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<string>> GetPolicyNamesAsync(CancellationToken cancellationToken = default)
    {
        var names = new HashSet<string>(
            await GetRegisteredPolicyNamesAsync(cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal
        );

        // Permission names are policies only through PermissionPolicyProvider, and only in their prefixed form.
        if (policyProvider is PermissionPolicyProvider)
        {
            var prefix = managementOptions.Value.PolicyNamePrefix;
            var permissionNames = await GetPermissionNamesAsync(cancellationToken).ConfigureAwait(false);

            names.UnionWith(prefix is null ? permissionNames : permissionNames.Select(name => prefix + name));
        }

        return names;
    }

    // ASP.NET Core exposes no listing of registered policies; this binds to the private PolicyMap getter.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_PolicyMap")]
    private static extern Dictionary<string, Task<AuthorizationPolicy?>> _GetPolicyMap(AuthorizationOptions options);
}
