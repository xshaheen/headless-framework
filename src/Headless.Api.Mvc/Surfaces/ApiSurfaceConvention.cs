// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api.Mvc.Surfaces;

/// <summary>Applies surface defaults while preserving controller/action overrides and API version groups.</summary>
public sealed class ApiSurfaceConvention(ApiSurfaceRegistry registry) : IApplicationModelConvention
{
    private readonly ApiSurfaceRegistry _registry = Argument.IsNotNull(registry);

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var surface = controller.Attributes.OfType<ApiSurfaceAttribute>().SingleOrDefault();
            if (surface is null)
            {
                continue;
            }

            var descriptor = _registry.GetRequiredSurface(surface.SurfaceName);
            if (!string.IsNullOrWhiteSpace(descriptor.RoutePrefix))
            {
                var prefix = new AttributeRouteModel(new RouteAttribute(descriptor.RoutePrefix));
                var hasAbsoluteActionRoute = controller
                    .Actions.SelectMany(x => x.Selectors)
                    .Any(x => x.AttributeRouteModel?.IsAbsoluteTemplate == true);
                foreach (var selector in controller.Selectors)
                {
                    if (selector.AttributeRouteModel?.IsAbsoluteTemplate == true || hasAbsoluteActionRoute)
                    {
                        throw new InvalidOperationException(
                            $"Controller '{controller.ControllerName}' uses an absolute route that escapes API surface '{descriptor.SurfaceName}'. Use relative routes."
                        );
                    }

                    selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(
                        prefix,
                        selector.AttributeRouteModel
                    );
                }
            }

            foreach (var action in controller.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    selector.EndpointMetadata.Insert(0, descriptor);
                    if (!string.IsNullOrWhiteSpace(descriptor.AuthorizationPolicy))
                    {
                        selector.EndpointMetadata.Insert(0, new AuthorizeAttribute(descriptor.AuthorizationPolicy));
                    }

                    var tenancy = ApiSurfaceEndpointDefaults.GetTenancyMetadata(
                        descriptor.TenancyMode,
                        controller.Attributes.Concat(selector.EndpointMetadata)
                    );
                    if (tenancy is not null)
                    {
                        selector.EndpointMetadata.Insert(0, tenancy);
                    }
                }
            }
        }
    }
}
