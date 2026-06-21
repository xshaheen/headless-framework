// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Models;

/// <summary>
/// The context object passed to <c>IPermissionStateProvider</c> implementations when they are asked to
/// evaluate whether a permission should be treated as enabled for the current request. Carries the DI container
/// and the permission being evaluated so providers can resolve additional services and inspect the definition.
/// </summary>
public sealed class PermissionStateContext
{
    /// <summary>The request-scoped service provider; use it to resolve per-request services.</summary>
    public required IServiceProvider ServiceProvider { get; set; }

    /// <summary>The permission definition whose enabled state is being evaluated.</summary>
    public required PermissionDefinition Permission { get; set; }
}
