// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;

namespace Headless.Permissions.Definitions;

public interface IPermissionDefinitionProvider
{
    void Define(IPermissionDefinitionContext context);
}
