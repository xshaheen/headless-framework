// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Surfaces;

/// <summary>
/// Selects default tenancy metadata for an API surface. Explicit endpoint metadata takes precedence.
/// </summary>
/// <remarks>Tenant requirements are enforced by the tenancy authorization handler only when the applicable
/// authorization policy includes TenantRequirement. These defaults do not register services or policies.</remarks>
public enum ApiSurfaceTenancyMode
{
    /// <summary>
    /// The surface does not apply default tenancy metadata. Endpoints or controllers define their own policy.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Adds a resolved-tenant requirement as default metadata, consumed by the tenancy authorization handler.
    /// </summary>
    RequireTenant = 1,

    /// <summary>
    /// Adds default metadata permitting a missing tenant when the tenancy authorization handler runs.
    /// </summary>
    AllowMissingTenant = 2,

    /// <summary>
    /// Adds default metadata to skip tenant resolution. This does not grant permission to omit a required tenant.
    /// </summary>
    SkipTenantResolution = 3,
}
