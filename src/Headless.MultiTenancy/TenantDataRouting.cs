// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// Records that a data context type is tenant-routed: each instance connects to its tenant's schema or database
/// from <see cref="ITenantDataPlacementResolver"/>. Registered as a singleton instance by the data-access
/// package's routing entry point (for example <c>RouteTenantData&lt;TContext&gt;()</c>) so packages that must never
/// run over a routed context can detect it without referencing that package.
/// </summary>
/// <param name="contextType">The routed data context type.</param>
[PublicAPI]
public sealed class TenantDataRoutedContextRegistration(Type contextType)
{
    /// <summary>The routed data context type.</summary>
    public Type ContextType { get; } = Argument.IsNotNull(contextType);
}
