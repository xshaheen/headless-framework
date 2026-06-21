// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;

namespace Headless.Permissions.GrantProviders;

/// <summary>
/// Base class for grant providers that delegate storage to <see cref="IPermissionGrantStore"/>.
/// Subclasses implement <see cref="CheckAsync(IReadOnlyCollection{PermissionDefinition}, ICurrentUser, CancellationToken)"/>
/// to translate the current principal into a provider key (user id, role name, etc.) and query the store.
/// <see cref="SetAsync(PermissionDefinition, string, bool, CancellationToken)"/> routes to
/// <see cref="IPermissionGrantStore.GrantAsync(string, string, string, string?, CancellationToken)"/> or
/// <see cref="IPermissionGrantStore.RevokeAsync(string, string, string, CancellationToken)"/> based on
/// <c>isGranted</c>.
/// </summary>
public abstract class StorePermissionGrantProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
    : IPermissionGrantProvider
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public async Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    )
    {
        var result = await CheckAsync([permission], currentUser, cancellationToken).ConfigureAwait(false);

        return result.First().Value;
    }

    public abstract Task<MultiplePermissionGrantStatusResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    );

    public Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return isGranted
            ? grantStore.GrantAsync(permission.Name, Name, providerKey, currentTenant.Id, cancellationToken)
            : grantStore.RevokeAsync(permission.Name, Name, providerKey, cancellationToken);
    }

    public Task SetAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var names = permissions.Select(x => x.Name).ToArray();

        return isGranted
            ? grantStore.GrantAsync(names, Name, providerKey, currentTenant.Id, cancellationToken)
            : grantStore.RevokeAsync(names, Name, providerKey, cancellationToken);
    }
}
