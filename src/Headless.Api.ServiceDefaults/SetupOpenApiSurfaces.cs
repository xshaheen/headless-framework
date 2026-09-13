// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Api;

[PublicAPI]
public static class SetupOpenApiSurfaces
{
    /// <summary>Registers built-in OpenAPI documents using the finalized settings of their API surfaces.</summary>
    /// <remarks>Omit names to infer all registered surface documents and close surface registration.
    /// Explicit names must match ApiSurfaceBuilder.OpenApi.DocumentName. Native MapOpenApi serves the documents.
    /// The callback deliberately exposes Microsoft.AspNetCore.OpenApi options for full provider customization.
    /// Its ShouldInclude predicate can narrow each surface independently of API Explorer version groups.</remarks>
    public static IServiceCollection AddHeadlessApiSurfaceDocuments(
        this IServiceCollection services,
        IEnumerable<string>? documentNames = null,
        Action<OpenApiOptions>? configure = null
    )
    {
        Argument.IsNotNull(services);
        documentNames ??= ApiSurfaceRegistration.InferDocumentNames(services);

        foreach (var documentName in documentNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Argument.IsNotNullOrWhiteSpace(documentName);
            // Native OpenAPI normalizes both registration keys and request document names.
            var name = documentName.ToLowerInvariant();
            services.AddOpenApi(name);
            services
                .AddOptions<OpenApiOptions>(name)
                .Configure<ApiSurfaceRegistry>(
                    (options, registry) =>
                    {
                        var surface = registry.GetRequiredSurfaceForDocument(name);
                        // The native predicate ties document names to API Explorer version groups.
                        options.ShouldInclude = _ => true;
                        options.AddDocumentTransformer(
                            (document, _, _) =>
                            {
                                document.Info.Title = surface.OpenApi.Title;
                                return Task.CompletedTask;
                            }
                        );
                        configure?.Invoke(options);
                        var include = options.ShouldInclude;
                        options.ShouldInclude = description =>
                            string.Equals(
                                description
                                    .ActionDescriptor.EndpointMetadata.OfType<IApiSurfaceMetadata>()
                                    .LastOrDefault()
                                    ?.SurfaceName,
                                surface.SurfaceName,
                                StringComparison.OrdinalIgnoreCase
                            ) && include(description);
                    }
                )
                .ValidateOnStart();
        }

        return services;
    }
}
