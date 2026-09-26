// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions.Grants;

/// <summary>
/// Reads which permissions a principal is granted and which named policies it satisfies, for example to include in
/// the configuration an application returns to its front end.
/// </summary>
[PublicAPI]
public interface IGrantedPoliciesReader
{
    /// <summary>Resolves what the principal in <paramref name="context"/> is granted.</summary>
    /// <param name="context">The principal and tenant to resolve the grants for.</param>
    /// <param name="policyNames">
    /// The named policies to evaluate through <see cref="IAuthorizationService"/>. The application chooses which
    /// policies the client sees; <see cref="Requirements.IAuthorizationPolicyCatalog"/> can supply the full list.
    /// Every defined permission is always checked.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>
    /// The granted permission names and the satisfied policy names. A name that is not granted is absent.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="policyNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A name in <paramref name="policyNames"/> is not a registered policy or a defined permission.</exception>
    Task<IReadOnlySet<string>> GetAsync(
        PrincipalContext context,
        IReadOnlyCollection<string> policyNames,
        CancellationToken cancellationToken = default
    );
}

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
