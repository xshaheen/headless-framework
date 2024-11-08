// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class UserPermissionValueProvider(
    IPermissionGrantStore permissionGrantStore,
    ICurrentTenant currentTenant
) : StorePermissionValueProvider(permissionGrantStore, currentTenant)
{
    private readonly IPermissionGrantStore _permissionGrantStore = permissionGrantStore;

    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<MultiplePermissionGrantResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        string providerName,
        CancellationToken cancellationToken = default
    )
    {
        var permissionNames = permissions.Select(x => x.Name).ToList();

        if (!string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            return new MultiplePermissionGrantResult(permissionNames);
        }

        var userId = currentUser.UserId?.ToString();

        if (userId is null)
        {
            return new MultiplePermissionGrantResult(permissionNames);
        }

        return await _permissionGrantStore.IsGrantedAsync(permissionNames, Name, userId, cancellationToken);
    }
}
