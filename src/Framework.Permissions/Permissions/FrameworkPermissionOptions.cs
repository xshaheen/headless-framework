// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Framework.Permissions.Permissions.Definitions;
using Framework.Permissions.Permissions.Values;

namespace Framework.Permissions.Permissions;

public class FrameworkPermissionOptions
{
    public TypeList<IPermissionDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IPermissionValueProvider> ValueProviders { get; } = [];
}
