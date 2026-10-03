// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions.Grants;

/// <summary>Default <see cref="IGrantedPoliciesReader"/>.</summary>
internal sealed class GrantedPoliciesReader(
    IPermissionManager permissionManager,
    IAuthorizationService authorizationService,
    ICurrentTenant currentTenant,
    // Optional: only Headless.Api.ServiceDefaults registers an accessor. The principal is passed explicitly to the
    // grant check and to IAuthorizationService, so the switch matters only to handlers that read the ambient one.
    ICurrentPrincipalAccessor? principalAccessor = null
) : IGrantedPoliciesReader
{
    public async Task<IReadOnlySet<string>> GetAsync(
        PrincipalContext context,
        IReadOnlyCollection<string> policyNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(policyNames);

        // Grant caching and tenant requirements read the ambient tenant, and policy handlers may read the ambient
        // principal, so evaluate under the context's identity.
        using var principalScope = principalAccessor?.Change(context.Principal);
        using var tenantScope = currentTenant.Change(context.TenantId);

        var granted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var policyName in policyNames.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await authorizationService.AuthorizeAsync(context.Principal, policyName).ConfigureAwait(false);

            if (result.Succeeded)
            {
                granted.Add(policyName);
            }
        }

        var permissions = await permissionManager
            .GetAllAsync(new PrincipalCurrentUser(context.Principal), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var permission in permissions)
        {
            if (permission.IsGranted)
            {
                granted.Add(permission.Name);
            }
        }

        return granted;
    }
}
