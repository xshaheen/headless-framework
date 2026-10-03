// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Read-only access to permission definitions declared in code via registered
/// <see cref="IPermissionDefinitionProvider"/> implementations.
/// </summary>
public interface IStaticPermissionDefinitionStore
{
    /// <summary>
    /// Finds a permission by name in the code-defined static store.
    /// Returns <see langword="null"/> if no such permission exists.
    /// </summary>
    Task<PermissionDefinition?> GetOrDefaultPermissionAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all statically-defined permissions, flattened across every group and nested child.</summary>
    Task<IReadOnlyCollection<PermissionDefinition>> GetAllPermissionsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all statically-defined permission groups.</summary>
    Task<IReadOnlyCollection<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
