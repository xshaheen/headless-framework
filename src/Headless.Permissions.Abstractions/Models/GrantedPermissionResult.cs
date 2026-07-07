// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>The outcome of resolving a single permission for a principal.</summary>
public sealed class GrantedPermissionResult(string name, bool isGranted)
{
    private readonly List<GrantPermissionProvider> _providers = [];

    /// <summary>The permission name this result is for.</summary>
    public string Name { get; } = Argument.IsNotNull(name);

    /// <summary>The effective decision after applying explicit-deny-overrides-grant resolution across all providers.</summary>
    public bool IsGranted { get; internal set; } = isGranted;

    /// <summary>
    /// The providers that contributed a grant to this result. Empty when the permission is not granted; an
    /// explicit denial does not appear here because it suppresses the grant entirely.
    /// </summary>
    public IReadOnlyList<GrantPermissionProvider> Providers => _providers;

    /// <summary>Records that <paramref name="provider"/> contributed a grant. Internal population path used by the framework.</summary>
    internal void AddProvider(GrantPermissionProvider provider) => _providers.Add(provider);
}

/// <summary>Identifies a grant provider and the specific keys under which it granted the permission.</summary>
public sealed class GrantPermissionProvider(string name, IReadOnlyCollection<string> keys)
{
    /// <summary>The grant provider name (for example <c>"User"</c> or <c>"Role"</c>).</summary>
    public string Name { get; } = Argument.IsNotNull(name);

    /// <summary>The provider keys that granted the permission (for example the granting role names for the Role provider).</summary>
    public IReadOnlyCollection<string> Keys { get; } = Argument.IsNotNull(keys);
}
