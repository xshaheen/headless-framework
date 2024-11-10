// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Framework.Permissions.Definitions;
using Framework.Permissions.GrantProviders;

namespace Framework.Permissions.Models;

public sealed class PermissionManagementProvidersOptions
{
    public TypeList<IPermissionDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IPermissionGrantProvider> GrantProviders { get; } = [];

    public HashSet<string> DeletedPermissionGroups { get; } = [];

    public HashSet<string> DeletedPermissions { get; } = [];
}
