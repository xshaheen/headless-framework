// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Marks an endpoint or controller as requiring a resolved tenant.
/// Overrides <see cref="AllowMissingTenantAttribute"/> when this attribute is registered later
/// in the endpoint metadata collection (last-wins semantics).
/// </summary>
/// <remarks>
/// Consumed by <c>TenantRequirementHandler</c> inside the ASP.NET Core authorization pipeline.
/// When the tenant is absent, the handler fails with a <c>g:tenant_required</c> 403 response.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class RequireTenantAttribute : Attribute;
