// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api.Surfaces;

// Native OpenAPI providers need document names before the service collection becomes read-only.
// Closing registration prevents later surfaces from silently missing their inferred documents.
internal sealed class ApiSurfaceRegistration
{
    internal static ApiSurfaceDescriptor[] GetSurfaces(IServiceCollection services) =>
        [
            .. services
                .Where(service => service.ServiceType == typeof(ApiSurfaceDescriptor))
                .Select(service => (ApiSurfaceDescriptor)service.ImplementationInstance!),
        ];

    internal static string[] InferDocumentNames(IServiceCollection services)
    {
        var names = GetSurfaces(services).Select(surface => surface.OpenApi.DocumentName).ToArray();
        services.TryAddSingleton(new ApiSurfaceRegistration());
        return names;
    }

    internal static void EnsureOpen(IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType == typeof(ApiSurfaceRegistration)))
        {
            throw new InvalidOperationException(
                "Register all API surfaces with AddHeadlessApiSurfaces before OpenAPI document inference (AddHeadless, AddHeadlessOpenApiSurfaces, or AddNswagOpenApiSurfaces)."
            );
        }
    }
}
