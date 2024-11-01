// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionProvider
{
    void Define(IPermissionDefinitionContext context);
}
