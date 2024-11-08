// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.ValueProviders;

public interface IPermissionValueProvider
{
    string Name { get; }

    Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        ICurrentUser currentUser,
        string providerName,
        CancellationToken cancellationToken = default
    );

    Task<MultiplePermissionGrantResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        string providerName,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );
}
