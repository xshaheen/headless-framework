// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Asp.Versioning.ApiExplorer;
using Framework.Api.ApiExplorer;
using Framework.BuildingBlocks;
using Framework.OpenApi.Swashbuckle.Extensions;
using Framework.OpenApi.Swashbuckle.OperationFilters;
using Framework.Primitives;
using Framework.Reflection;
using MicroElements.Swashbuckle.FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Framework.OpenApi.Swashbuckle;

[PublicAPI]
public static class AddSwashbuckleSwaggerExtensions
{
    // TODO: Add options to configure Swashbuckle Swagger

    public static IEndpointConventionBuilder UseFrameworkSwashbuckleSwagger(this WebApplication app)
    {
        const string uiPath = "swashbuckle";
        const string documentRoute = "/swashbuckle/{documentName}/swagger.{extension:regex(^(json|ya?ml)$)}";

        var endpointBuilder = app.MapSwagger(documentRoute);

        app.UseSwagger(options =>
        {
            options.RouteTemplate = documentRoute;
        });

        app.UseSwaggerUI(options =>
        {
            var productName = AssemblyInformation.Entry.Product?.Replace('.', ' ');
            options.DocumentTitle = productName is null ? "API" : productName + " API"; // Set the Swagger UI browser document title.
            options.RoutePrefix = uiPath; // Set the Swagger UI to render at '/swashbuckle'.
            options.DocExpansion(DocExpansion.None);
            options.DisplayOperationId();
            options.DisplayRequestDuration();

            var apiVersionDescriptions = app
                .Services.GetRequiredService<IApiVersionDescriptionProvider>()
                .ApiVersionDescriptions.OrderByDescending(v => v.ApiVersion);

            foreach (var apiVersionDescription in apiVersionDescriptions)
            {
                var version = apiVersionDescription.ApiVersion.ToString(format: null, CultureInfo.InvariantCulture);

                options.SwaggerEndpoint(
                    url: $"/swashbuckle/{apiVersionDescription.GroupName}/swagger.json",
                    name: $"Version {version}"
                );
            }
        });

        return endpointBuilder;
    }

    internal static IServiceCollection AddFrameworkSwashbuckleSwagger(this IServiceCollection services)
    {
        services.AddFluentValidationRulesToSwagger();
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<ApiVersionOperationFilter>();
            options.OperationFilter<UnauthorizedResponseOperationFilter>();
            options.OperationFilter<ForbiddenResponseOperationFilter>();
            options.OperationFilter<ClaimsOperationFilter>();
            options.OperationFilter<ProblemDetailsOperationFilter>();

            options.AddBuildingBlocksPrimitiveMappings();
            options.AddAllPrimitivesSwaggerMappings();

            options.EnableAnnotations();
            options.SupportNonNullableReferenceTypes();
            options.DescribeAllParametersInCamelCase();

            var assembly = Assembly.GetEntryAssembly();

            if (assembly is not null)
            {
                // Add the XML comment file for this assembly, so its contents can be displayed.
                options.IncludeXmlCommentsIfExists(assembly);
            }

            options.CustomOperationIds(api =>
                api.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor
                    ? $"{controllerActionDescriptor.ControllerName}_{controllerActionDescriptor.ActionName}"
                    : throw new InvalidOperationException("Unable to determine operation id for endpoint.")
            );

            // https://rimdev.io/swagger-grouping-with-controller-name-fallback-using-swashbuckle-aspnetcore/
            options.TagActionsBy(api =>
            {
                if (api.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
                {
                    return [controllerActionDescriptor.ControllerName];
                }

                if (api.GroupName is not null)
                {
                    return [api.GroupName];
                }

                throw new InvalidOperationException("Unable to determine tag for endpoint.");
            });

            options.AddSecurityDefinition(_BearerDefinitionName, _GetBearerSecurityDefinition());
            options.AddSecurityDefinition(_ApiKeyDefinitionName, _CreateApiKeySecurityDefinition());

            options.AddSecurityRequirement(
                new()
                {
                    [_CreateBearerSecurityRequirement()] = Array.Empty<string>(),
                    [_CreateApiKeySecurityRequirement()] = Array.Empty<string>(),
                }
            );
        });

        services.ConfigureOptions<ConfigureSwashbuckleSwaggerGenApiVersionsOptions>();

        return services;
    }

    #region Primitives

    /// <summary>Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation.</summary>
    /// <param name="options">The SwaggerGenOptions instance to which mappings are added.</param>
    /// <remarks>
    /// The method adds Swagger mappings for the following types:
    /// <see cref="Money" />
    /// <see cref="Month" />
    /// <see cref="AccountId" />
    /// <see cref="UserId" />
    /// </remarks>
    public static void AddBuildingBlocksPrimitiveMappings(this SwaggerGenOptions options)
    {
        options.MapType<Money>(
            () =>
                new OpenApiSchema
                {
                    Type = "number",
                    Format = "decimal",
                    Title = "Money",
                }
        );
        options.MapType<Money?>(
            () =>
                new OpenApiSchema
                {
                    Type = "number",
                    Format = "decimal",
                    Nullable = true,
                    Title = "Nullable<Money>",
                }
        );
        options.MapType<Month>(
            () =>
                new OpenApiSchema
                {
                    Type = "integer",
                    Format = "int32",
                    Title = "Month",
                }
        );
        options.MapType<Month?>(
            () =>
                new OpenApiSchema
                {
                    Type = "integer",
                    Format = "int32",
                    Nullable = true,
                    Title = "Nullable<Month>",
                }
        );
        options.MapType<AccountId>(() => new OpenApiSchema { Type = "string", Title = "AccountId" });
        options.MapType<UserId>(() => new OpenApiSchema { Type = "string", Title = "UserId" });
    }

    #endregion

    #region Api Versioning

    private sealed class ConfigureSwashbuckleSwaggerGenApiVersionsOptions(
        IApiVersionDescriptionProvider apiVersionDescriptionProvider
    ) : IConfigureOptions<SwaggerGenOptions>
    {
        public void Configure(SwaggerGenOptions options)
        {
            // Add api versioning to swagger
            foreach (var apiVersionDescription in apiVersionDescriptionProvider.ApiVersionDescriptions)
            {
                options.SwaggerDoc(apiVersionDescription.GroupName, _CreateInfo(apiVersionDescription));
            }
        }

        private static OpenApiInfo _CreateInfo(ApiVersionDescription apiVersionDescription)
        {
            var openApiInfo = new OpenApiInfo
            {
                Version = apiVersionDescription.ApiVersion.ToString(),
                Title = AssemblyInformation.Entry.Product,
                Description = SwaggerInformation.ResponsesDescription,
                Contact = new OpenApiContact { Name = "Zad Digital", Email = "contact@zad.digital" },
                TermsOfService = new Uri("https://www.zad.digital/terms-of-service"),
            };

            if (apiVersionDescription.IsDeprecated)
            {
                openApiInfo.Description = " This API version has been deprecated.\n" + openApiInfo.Description;
            }

            return openApiInfo;
        }
    }

    #endregion

    #region ApiKey

    private const string _ApiKeyHeaderName = HttpHeaderNames.ApiKey;
    private const string _ApiKeyDefinitionName = "ApiKey";

    private static OpenApiSecurityScheme _CreateApiKeySecurityDefinition()
    {
        return new OpenApiSecurityScheme
        {
            Name = _ApiKeyHeaderName,
            Scheme = "apikey",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Description = $"Api key needed to access the endpoints. {_ApiKeyHeaderName}: My_API_Key",
        };
    }

    private static OpenApiSecurityScheme _CreateApiKeySecurityRequirement()
    {
        return new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Id = _ApiKeyDefinitionName, Type = ReferenceType.SecurityScheme },
        };
    }

    #endregion

    #region Bearer

    private const string _BearerHeaderName = HttpHeaderNames.Authorization;
    private const string _BearerDefinitionName = "Bearer";

    private static OpenApiSecurityScheme _GetBearerSecurityDefinition()
    {
        return new OpenApiSecurityScheme
        {
            Name = _BearerHeaderName,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Description = """
                JWT Authorization header using the Bearer scheme.
                Enter 'Bearer' [space] and then your token in the text input below.
                Example: 'Bearer <token>'
                """,
        };
    }

    private static OpenApiSecurityScheme _CreateBearerSecurityRequirement()
    {
        return new()
        {
            Reference = new OpenApiReference { Id = _BearerDefinitionName, Type = ReferenceType.SecurityScheme },
        };
    }

    #endregion
}
