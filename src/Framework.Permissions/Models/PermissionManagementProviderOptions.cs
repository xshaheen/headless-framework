// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Framework.Permissions.Definitions;
using Framework.Permissions.Values;

namespace Framework.Permissions.Models;

public sealed class PermissionManagementProviderOptions
{
    public TypeList<IPermissionDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IPermissionValueProvider> ValueProviders { get; } = [];

    public List<string> DeletedPermissionGroups { get; } = [];

    public List<string> DeletedPermissions { get; } = [];
}
