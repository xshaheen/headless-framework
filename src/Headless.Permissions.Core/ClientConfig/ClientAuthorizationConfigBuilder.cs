// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Permissions.Grants;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Permissions.ClientConfig;

/// <summary>
/// Builds the authorization section of an application's client config: the permissions the principal is granted
/// and the named policies it satisfies.
/// </summary>
[PublicAPI]
public interface IClientAuthorizationConfigBuilder
{
    /// <summary>Resolves what the principal in <paramref name="context"/> is granted.</summary>
    /// <param name="context">The principal and tenant to resolve the grants for.</param>
    /// <param name="policyNames">
    /// The named policies to evaluate through <see cref="IAuthorizationService"/>. The application chooses which
    /// policies the client sees; <see cref="Requirements.IAuthorizationPolicyCatalog"/> can supply the full list.
    /// Every defined permission is always checked.
    /// </param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The granted permissions and satisfied policies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="policyNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A name in <paramref name="policyNames"/> is not a registered policy or a defined permission.</exception>
    Task<ClientAuthorizationConfig> BuildAsync(
        ClientConfigContext context,
        IReadOnlyCollection<string> policyNames,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The authorization section of a client config.</summary>
/// <param name="GrantedPolicies">
/// The granted permission names and satisfied policy names, each mapped to <see langword="true"/>. Names that are
/// not granted are absent rather than mapped to <see langword="false"/>.
/// </param>
[PublicAPI]
public sealed record ClientAuthorizationConfig(IReadOnlyDictionary<string, bool> GrantedPolicies);

/// <summary>Default <see cref="IClientAuthorizationConfigBuilder"/>.</summary>
internal sealed class ClientAuthorizationConfigBuilder(
    IPermissionManager permissionManager,
    IAuthorizationService authorizationService,
    ICurrentPrincipalAccessor principalAccessor,
    ICurrentTenant currentTenant
) : IClientAuthorizationConfigBuilder
{
    public async Task<ClientAuthorizationConfig> BuildAsync(
        ClientConfigContext context,
        IReadOnlyCollection<string> policyNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(policyNames);

        // Grant caching and tenant requirements read the ambient tenant, and policy handlers may read the ambient
        // principal, so evaluate under the context's identity.
        using var principalScope = principalAccessor.Change(context.Principal);
        using var tenantScope = currentTenant.Change(context.TenantId);

        var granted = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var policyName in policyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (granted.ContainsKey(policyName))
            {
                continue;
            }

            var result = await authorizationService.AuthorizeAsync(context.Principal, policyName).ConfigureAwait(false);

            if (result.Succeeded)
            {
                granted[policyName] = true;
            }
        }

        var permissions = await permissionManager
            .GetAllAsync(new PrincipalCurrentUser(context.Principal), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var permission in permissions)
        {
            if (permission.IsGranted)
            {
                granted[permission.Name] = true;
            }
        }

        return new ClientAuthorizationConfig(granted);
    }
}
