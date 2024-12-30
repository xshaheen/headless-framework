// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.GrantProviders;

[PublicAPI]
public sealed class UserPermissionGrantProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
    : StorePermissionGrantProvider(grantStore, currentTenant)
{
    private readonly IPermissionGrantStore _grantStore = grantStore;

    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<MultiplePermissionGrantStatusResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    )
    {
        var permissionNames = permissions.Select(x => x.Name).ToList();
        var userId = currentUser.UserId?.ToString();

        if (userId is null)
        {
            return new MultiplePermissionGrantStatusResult(permissionNames, [], PermissionGrantStatus.Undefined);
        }

        var result = new MultiplePermissionGrantStatusResult();
        var statusMap = await _grantStore.IsGrantedAsync(permissionNames, Name, userId, cancellationToken);

        foreach (var permission in permissions)
        {
            string[] providerKeys = [userId];

            if (!statusMap.TryGetValue(permission.Name, out var status))
            {
                result.Add(permission.Name, PermissionGrantResult.Undefined(providerKeys));

                continue;
            }

            var permissionGrantResult = status switch
            {
                PermissionGrantStatus.Granted => PermissionGrantResult.Granted(providerKeys),
                PermissionGrantStatus.Prohibited => PermissionGrantResult.Prohibited(providerKeys),
                _ => PermissionGrantResult.Undefined(providerKeys),
            };

            result.Add(permission.Name, permissionGrantResult);
        }

        return result;
    }
}
