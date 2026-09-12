// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.OpenApi.Nswag;

public static class SetupNswagSurfaces
{
    /// <summary>
    /// Registers NSwag OpenAPI documents for each configured API surface in <see cref="ApiSurfaceOptions"/>,
    /// applying standard Headless defaults (problem details, operation processors, type mappers).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="setupHeadlessAction">Optional callback for configuring Headless NSwag options.</param>
    /// <param name="setupGeneratorActions">Optional callback for configuring generator settings per surface.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNswagOpenApiSurfaces(
        this IServiceCollection services,
        Action<HeadlessNswagOptions>? setupHeadlessAction = null,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, ApiSurfaceDescriptor>? setupGeneratorActions = null
    )
    {
        Argument.IsNotNull(services);

        var headlessOptions = new HeadlessNswagOptions();
        setupHeadlessAction?.Invoke(headlessOptions);

        var surfaceOptions = services.BuildServiceProvider().GetRequiredService<IOptions<ApiSurfaceOptions>>().Value;

        foreach (var surface in surfaceOptions.Surfaces)
        {
            var surfaceDescriptor = surface;
            services.AddOpenApiDocument(
                (settings, sp) =>
                {
                    SetupNswag.ConfigureGeneratorSettings(settings, sp, headlessOptions);
                    settings.DocumentName = surfaceDescriptor.DocumentName;
                    settings.ApiGroupNames = [surfaceDescriptor.GroupName];
                    settings.Title = surfaceDescriptor.DocumentTitle;

                    setupGeneratorActions?.Invoke(settings, surfaceDescriptor);

                    SetupNswag.ConfigureHeadlessGeneratorSettings(settings, headlessOptions);

                    if (surfaceDescriptor.TenancyPosture == SurfaceTenancyPosture.RequireTenant)
                    {
                        settings.OperationProcessors.Add(
                            new Surfaces.TenantRequiredForbiddenExampleOperationProcessor(
                                includeTenantRequiredExample: true
                            )
                        );
                    }
                }
            );
        }

        return services;
    }

    /// <summary>
    /// Mounts OpenAPI endpoints for each configured surface at <c>/openapi/{surface.DocumentName}.json</c>
    /// and configures Swagger UI at <c>/swagger</c> exposing all surfaces.
    /// </summary>
    public static WebApplication MapNswagOpenApiSurfaces(
        this WebApplication app,
        Action<OpenApiDocumentMiddlewareSettings>? documentSettings = null,
        Action<SwaggerUiSettings>? uiSettings = null
    )
    {
        Argument.IsNotNull(app);

        var surfaceOptions = app.Services.GetRequiredService<IOptions<ApiSurfaceOptions>>().Value;

        foreach (var surface in surfaceOptions.Surfaces)
        {
            app.UseOpenApi(settings =>
            {
                settings.DocumentName = surface.DocumentName;
                settings.Path = $"/openapi/{surface.DocumentName}.json";
                documentSettings?.Invoke(settings);
            });
        }

        app.UseSwaggerUi(config =>
        {
            config.Path = "/swagger";
            config.DocumentPath = "/openapi/{documentName}.json";
            config.PersistAuthorization = true;
            config.EnableTryItOut = true;
            config.TagsSorter = "alpha";
            config.DocExpansion = "none";
            uiSettings?.Invoke(config);
        });

        return app;
    }
}
