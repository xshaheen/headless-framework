// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Mvc.Surfaces;

/// <summary>
/// Application model convention that applies route prefixes, authorization policies,
/// ApiExplorer group names, and tenancy metadata based on <see cref="ApiSurfaceAttribute"/>
/// and configured <see cref="ApiSurfaceOptions"/>.
/// </summary>
public sealed class ApiSurfaceConvention(IOptions<ApiSurfaceOptions> options) : IApplicationModelConvention
{
    private readonly ApiSurfaceOptions _options = Argument.IsNotNull(options).Value;

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var surfaceAttr = controller.Attributes.OfType<ApiSurfaceAttribute>().FirstOrDefault();
            if (surfaceAttr is null)
            {
                continue;
            }

            if (!_options.TryGetSurface(surfaceAttr.SurfaceName, out var descriptor) || descriptor is null)
            {
                continue;
            }

            // Set ApiExplorer GroupName for OpenAPI grouping
            if (!string.IsNullOrWhiteSpace(descriptor.GroupName))
            {
                controller.ApiExplorer.GroupName ??= descriptor.GroupName;
            }

            // Apply route prefix if defined
            if (!string.IsNullOrWhiteSpace(descriptor.RoutePrefix))
            {
                var prefixModel = new AttributeRouteModel(
                    new Microsoft.AspNetCore.Mvc.RouteAttribute(descriptor.RoutePrefix.Trim('/'))
                );
                foreach (var selector in controller.Selectors)
                {
                    selector.AttributeRouteModel = selector.AttributeRouteModel is not null
                        ? AttributeRouteModel.CombineAttributeRouteModel(prefixModel, selector.AttributeRouteModel)
                        : prefixModel;
                }
            }

            // Apply default authorization policy if defined
            if (!string.IsNullOrWhiteSpace(descriptor.RequiredPolicy))
            {
                var authAttribute = new AuthorizeAttribute(descriptor.RequiredPolicy);
                foreach (var action in controller.Actions)
                {
                    foreach (var selector in action.Selectors)
                    {
                        selector.EndpointMetadata.Add(authAttribute);
                    }
                }
            }

            // Apply tenancy posture if configured
            _ApplyTenancyPosture(controller, descriptor.TenancyPosture);
        }
    }

    private static void _ApplyTenancyPosture(ControllerModel controller, SurfaceTenancyPosture posture)
    {
        object? tenancyAttribute = posture switch
        {
            SurfaceTenancyPosture.RequireTenant => new RequireTenantAttribute(),
            SurfaceTenancyPosture.AllowMissingTenant => new AllowMissingTenantAttribute(),
            SurfaceTenancyPosture.SkipTenantResolution => new SkipTenantResolutionAttribute(),
            _ => null,
        };

        if (tenancyAttribute is null)
        {
            return;
        }

        foreach (var action in controller.Actions)
        {
            foreach (var selector in action.Selectors)
            {
                selector.EndpointMetadata.Add(tenancyAttribute);
            }
        }
    }
}
