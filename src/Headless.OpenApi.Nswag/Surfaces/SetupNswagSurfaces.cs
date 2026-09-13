// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Headless.OpenApi.Nswag.Surfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.OpenApi.Nswag;

[PublicAPI]
public static class SetupNswagSurfaces
{
    /// <summary>Registers inferred or explicitly selected surface documents.</summary>
    /// <remarks>Omit names to infer all registered surface documents and close surface registration.
    /// Explicit names must match ApiSurfaceBuilder.OpenApi.DocumentName. ApiGroupNames can independently select API versions.</remarks>
    public static IServiceCollection AddNswagApiSurfaceDocuments(
        this IServiceCollection services,
        IEnumerable<string>? documentNames = null,
        Action<HeadlessNswagOptions>? setupHeadlessAction = null,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, ApiSurfaceDescriptor>? setupGeneratorActions = null
    )
    {
        Argument.IsNotNull(services);
        documentNames ??= ApiSurfaceRegistration.InferDocumentNames(services);
        var headlessOptions = new HeadlessNswagOptions();
        setupHeadlessAction?.Invoke(headlessOptions);
        foreach (var documentName in documentNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Argument.IsNotNullOrWhiteSpace(documentName);
            services.AddOpenApiDocument(
                (settings, sp) =>
                {
                    var surface = sp.GetRequiredService<ApiSurfaceRegistry>()
                        .GetRequiredSurfaceForDocument(documentName);
                    SetupNswag.ConfigureGeneratorSettings(settings, sp, headlessOptions);
                    settings.DocumentName = surface.OpenApi.DocumentName;
                    settings.Title = surface.OpenApi.Title;
                    setupGeneratorActions?.Invoke(settings, surface);
                    if (!string.Equals(settings.DocumentName, surface.OpenApi.DocumentName, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Configure surface document names through ApiSurfaceBuilder.OpenApi.DocumentName."
                        );
                    }

                    SetupNswag.ConfigureHeadlessGeneratorSettings(settings, headlessOptions);
                    // Filter before schema generation so excluded surfaces cannot leak schemas into this document.
                    settings.OperationProcessors.Insert(0, new ApiSurfaceOperationProcessor(surface.SurfaceName));
                }
            );
        }
        return services;
    }

    /// <summary>Serves registered documents at /openapi/{documentName}.json and Swagger UI at /swagger.</summary>
    public static WebApplication MapNswagApiSurfaceDocuments(
        this WebApplication app,
        Action<OpenApiDocumentMiddlewareSettings>? documentSettings = null,
        Action<SwaggerUiSettings>? uiSettings = null
    )
    {
        Argument.IsNotNull(app);
        return app.MapNswagOpenApi(documentSettings, uiSettings);
    }
}
