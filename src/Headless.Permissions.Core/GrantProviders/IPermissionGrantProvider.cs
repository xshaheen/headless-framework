// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Models;

namespace Headless.Permissions.GrantProviders;

public interface IPermissionGrantProvider
{
    string Name { get; }

    Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    );

    Task<MultiplePermissionGrantStatusResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );
}
