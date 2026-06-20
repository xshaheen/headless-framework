// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Permissions.Definitions;
using Headless.Permissions.GrantProviders;

namespace Headless.Permissions.Models;

/// <summary>
/// Registrar for the provider types that participate in permission definition and grant evaluation, and for
/// explicit tombstone sets used to clean up stale DB records.
/// </summary>
public sealed class PermissionManagementProvidersOptions
{
    /// <summary>
    /// Ordered list of <see cref="IPermissionDefinitionProvider"/> types whose <c>Define</c> methods are called
    /// during static store initialization to build the in-memory permission tree.
    /// </summary>
    public TypeList<IPermissionDefinitionProvider> DefinitionProviders { get; } = [];

    /// <summary>
    /// Ordered list of <see cref="IPermissionGrantProvider"/> types consulted during grant evaluation.
    /// Providers are called in order; a <see cref="PermissionGrantStatus.Prohibited"/> result from any provider
    /// stops evaluation.
    /// </summary>
    public TypeList<IPermissionGrantProvider> GrantProviders { get; } = [];

    /// <summary>
    /// Names of permission groups that have been removed from code and should be deleted from the database on the
    /// next <see cref="Definitions.IDynamicPermissionDefinitionStore.SaveAsync"/> call. All permissions belonging
    /// to a listed group are also deleted.
    /// </summary>
    public HashSet<string> DeletedPermissionGroups { get; } = [];

    /// <summary>
    /// Names of individual permissions that have been removed from code and should be deleted from the database on
    /// the next <see cref="Definitions.IDynamicPermissionDefinitionStore.SaveAsync"/> call.
    /// </summary>
    public HashSet<string> DeletedPermissions { get; } = [];
}
