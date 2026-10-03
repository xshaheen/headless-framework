// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>Identifies a grant provider and the specific keys under which it granted the permission.</summary>
public sealed class GrantPermissionProvider(string name, IReadOnlyCollection<string> keys)
{
    /// <summary>The grant provider name (for example <c>"User"</c> or <c>"Role"</c>).</summary>
    public string Name { get; } = Argument.IsNotNull(name);

    /// <summary>The provider keys that granted the permission (for example the granting role names for the Role provider).</summary>
    public IReadOnlyCollection<string> Keys { get; } = Argument.IsNotNull(keys);
}
