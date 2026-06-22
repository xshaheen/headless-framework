// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Models;

/// <summary>
/// Implemented by containers that can host permissions — a <see cref="PermissionGroupDefinition"/> (top-level
/// permissions) and a <see cref="PermissionDefinition"/> (nested child permissions) — so both can be built with
/// the same fluent <c>AddChild</c> call.
/// </summary>
public interface ICanAddChildPermission
{
    /// <summary>Creates a permission under this container and returns it so further children can be chained onto it.</summary>
    /// <param name="name">Unique permission name.</param>
    /// <param name="displayName">Optional localized display name; defaults to <paramref name="name"/> when omitted.</param>
    /// <param name="isEnabled">Whether the permission starts enabled. A disabled permission can never be granted.</param>
    PermissionDefinition AddChild(string name, string? displayName = null, bool isEnabled = true);
}
