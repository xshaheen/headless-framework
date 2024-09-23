// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Permissions.Permissions.Definitions;

public static class PermissionDefinitionContextExtensions
{
    /// <summary>
    /// Finds and disables a permission with the given <paramref name="name"/>.
    /// Returns false if given permission was not found.
    /// </summary>
    /// <param name="context">Permission definition context</param>
    /// <param name="name">Name of the permission</param>
    /// <returns>
    /// Returns true if given permission was found.
    /// Returns false if given permission was not found.
    /// </returns>
    public static bool TryDisablePermission(this IPermissionDefinitionContext context, string name)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(name);

        var permission = context.GetPermissionOrNull(name);

        if (permission is null)
        {
            return false;
        }

        permission.IsEnabled = false;

        return true;
    }
}
