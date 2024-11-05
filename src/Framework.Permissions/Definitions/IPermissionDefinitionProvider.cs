// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Models;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionProvider
{
    void Define(IPermissionDefinitionContext context);
}
