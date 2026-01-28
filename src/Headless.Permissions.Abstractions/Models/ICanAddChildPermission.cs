// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Models;

public interface ICanAddChildPermission
{
    PermissionDefinition AddChild(string name, string? displayName = null, bool isEnabled = true);
}
