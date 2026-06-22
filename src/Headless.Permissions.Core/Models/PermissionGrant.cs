// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// An immutable (name, isGranted) pair returned by grant providers. A value of <see langword="true"/> for
/// <see cref="IsGranted"/> means the permission is explicitly granted; <see langword="false"/> means it is
/// explicitly denied. The absence of a <see cref="PermissionGrant"/> entry for a given name is different from
/// an explicit denial — it corresponds to <see cref="PermissionGrantStatus.Undefined"/>.
/// </summary>
public sealed class PermissionGrant(string name, bool isGranted)
{
    /// <summary>The unique permission name this grant applies to.</summary>
    public string Name { get; } = Argument.IsNotNull(name);

    /// <summary><see langword="true"/> if the permission is granted; <see langword="false"/> if it is explicitly denied.</summary>
    public bool IsGranted { get; } = isGranted;
}
