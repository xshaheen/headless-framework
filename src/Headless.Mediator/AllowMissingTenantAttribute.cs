// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Mediator;

/// <summary>
/// Marks a Mediator request as allowed to run without an ambient tenant context.
/// </summary>
/// <remarks>
/// Apply this marker to host-level, public, or system requests that intentionally
/// execute outside a tenant scope.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class AllowMissingTenantAttribute : Attribute;
