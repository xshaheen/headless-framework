// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Marks an endpoint or controller as allowed to proceed even when no tenant is resolved.
/// Takes precedence over <see cref="RequireTenantAttribute"/> when this attribute is registered
/// later in the endpoint metadata collection (last-wins semantics).
/// </summary>
/// <remarks>
/// Consumed by <c>TenantRequirementHandler</c> inside the ASP.NET Core authorization pipeline.
/// For endpoints that must skip tenant resolution entirely (e.g., authentication endpoints),
/// prefer <see cref="SkipTenantResolutionAttribute"/> instead.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class AllowMissingTenantAttribute : Attribute;
