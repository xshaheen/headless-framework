// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Microsoft.Extensions.DependencyInjection;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;

namespace Headless.OpenApi.Nswag.Surfaces;

internal sealed class SurfaceDocumentRegistration
{
    internal static void AddSingle(
        IServiceCollection services,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider> configure
    )
    {
        services.AddOpenApiDocument(configure);
        var descriptor = services.Last(x => x.ServiceType == typeof(OpenApiDocumentRegistration));
        services.AddSingleton(new SingleDocumentSource(descriptor));
    }

    internal static void AddSurfaces(
        IServiceCollection services,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider, ApiSurfaceDescriptor> configure
    )
    {
        if (services.Any(x => x.ServiceType == typeof(SurfaceDocumentRegistration)))
        {
            throw new InvalidOperationException("AddNswagOpenApiSurfaces can only be registered once.");
        }

        services.AddSingleton<SurfaceDocumentRegistration>();
        // Retain NSwag's generator, MVC, and tooling services, but not its placeholder document.
        services.AddOpenApiDocument();
        services.Remove(services.Last(x => x.ServiceType == typeof(OpenApiDocumentRegistration)));
        services.AddSingleton<IEnumerable<OpenApiDocumentRegistration>>(provider =>
        {
            var singleSources = provider.GetServices<SingleDocumentSource>().ToArray();
            if (
                services.Any(x =>
                    x.ServiceType == typeof(OpenApiDocumentRegistration)
                    && !singleSources.Any(source => ReferenceEquals(source.Descriptor, x))
                )
            )
            {
                throw new InvalidOperationException(
                    "When API surfaces are enabled, register additional documents with AddNswagOpenApi, not AddOpenApiDocument."
                );
            }

            var documents = singleSources.Select(source => source.Create(provider)).ToList();
            foreach (var surface in provider.GetRequiredService<ApiSurfaceRegistry>().Surfaces)
            {
                // Reuse NSwag's serializer/MVC initialization against the real host provider.
                // This collection describes one factory; no second service provider is built.
                var factoryServices = new ServiceCollection();
                factoryServices.AddOpenApiDocument((settings, sp) => configure(settings, sp, surface));
                var factory = factoryServices.Single(x => x.ServiceType == typeof(OpenApiDocumentRegistration));
                documents.Add((OpenApiDocumentRegistration)factory.ImplementationFactory!(provider));
            }

            var duplicate = documents
                .GroupBy(x => x.DocumentName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Skip(1).Any());
            if (duplicate is not null)
            {
                throw new InvalidOperationException(
                    $"OpenAPI document '{duplicate.Key}' is registered more than once."
                );
            }

            return documents.ToArray();
        });
    }

    private sealed class SingleDocumentSource(ServiceDescriptor descriptor)
    {
        internal ServiceDescriptor Descriptor { get; } = descriptor;

        internal OpenApiDocumentRegistration Create(IServiceProvider provider) =>
            (OpenApiDocumentRegistration)Descriptor.ImplementationFactory!(provider);
    }
}
