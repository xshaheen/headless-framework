// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;

namespace Headless.Api.Surfaces;

internal static class ApiSurfaceEndpointDefaults
{
    internal static object? GetTenancyMetadata(ApiSurfaceTenancyMode mode, IEnumerable<object> metadata)
    {
        // Explicit endpoint/controller choices outrank the surface. Skip resolution is independent
        // of permission to omit a tenant, so it does not cancel an explicit requirement.
        var skipResolution = false;
        foreach (var item in metadata)
        {
            if (item is RequireTenantAttribute or AllowMissingTenantAttribute)
            {
                return null;
            }

            skipResolution |= item is SkipTenantResolutionAttribute;
        }

        return mode switch
        {
            ApiSurfaceTenancyMode.RequireTenant => new RequireTenantAttribute(),
            ApiSurfaceTenancyMode.AllowMissingTenant => new AllowMissingTenantAttribute(),
            ApiSurfaceTenancyMode.SkipTenantResolution when !skipResolution => new SkipTenantResolutionAttribute(),
            _ => null,
        };
    }
}
