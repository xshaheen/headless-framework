// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Permissions.Definitions;

public interface ICanAddChildPermission
{
    PermissionDefinition AddPermission(string name, string? displayName = null, bool isEnabled = true);
}
