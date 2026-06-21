// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Contributes permission groups and permissions to the framework's static definition set. Implement this
/// in your application and register it with <c>AddPermissionDefinitionProvider&lt;T&gt;()</c>; the framework
/// invokes <see cref="Define"/> once during static-store initialization.
/// </summary>
public interface IPermissionDefinitionProvider
{
    /// <summary>
    /// Declares this provider's groups and permissions by mutating <paramref name="context"/> (for example
    /// <c>context.AddGroup("Orders").AddChild("Orders.Edit")</c>). Called once when the static store is built.
    /// </summary>
    void Define(IPermissionDefinitionContext context);
}
