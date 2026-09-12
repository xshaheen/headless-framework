// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Surfaces;

/// <summary>
/// Dictates how multi-tenancy requirements are applied to endpoints in an API surface.
/// </summary>
public enum SurfaceTenancyPosture
{
    /// <summary>
    /// The surface does not apply default tenancy metadata. Endpoints or controllers define their own policy.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// All endpoints in this surface require a resolved tenant by default.
    /// </summary>
    RequireTenant = 1,

    /// <summary>
    /// All endpoints in this surface allow a missing tenant by default.
    /// </summary>
    AllowMissingTenant = 2,

    /// <summary>
    /// All endpoints in this surface skip tenant resolution completely by default.
    /// </summary>
    SkipTenantResolution = 3,
}
