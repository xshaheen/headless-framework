// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning.ApiExplorer;
using Framework.Api.ApiExplorer;
using Framework.Constants;
using Framework.OpenApi.Nswag.OperationProcessors;
using Framework.OpenApi.Nswag.SchemaProcessors;
using Framework.Primitives;
using Framework.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Generation.TypeMappers;
using NSwag;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors.Security;

namespace Framework.OpenApi.Nswag;

[PublicAPI]
public static class AddNswagSwaggerExtensions
{
    #region Add

    public static IServiceCollection AddFrameworkNswagOpenApi(
        this IServiceCollection services,
        Action<FrameworkNswagOptions>? setupFrameworkAction = null,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings>? setupGeneratorActions = null
    )
    {
        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                _ConfigureGeneratorSettings(settings, serviceProvider);
                setupGeneratorActions?.Invoke(settings);
                _ConfigureGeneratorSettingsByFramework(settings, setupFrameworkAction);
            }
        );

        return services;
    }

    public static IServiceCollection AddFrameworkNswagOpenApi(
        this IServiceCollection services,
        Action<FrameworkNswagOptions>? setupFrameworkAction,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider> setupGeneratorActions
    )
    {
        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                _ConfigureGeneratorSettings(settings, serviceProvider);
                setupGeneratorActions?.Invoke(settings, serviceProvider);
                _ConfigureGeneratorSettingsByFramework(settings, setupFrameworkAction);
            }
        );

        return services;
    }

    #endregion

    #region Map

    public static WebApplication MapFrameworkNswagOpenApiVersions(
        this WebApplication app,
        Action<OpenApiDocumentMiddlewareSettings, ApiVersionDescription>? documentSettings = null,
        Action<SwaggerUiSettings>? uiSettings = null
    )
    {
        foreach (var apiVersionDescription in app.DescribeApiVersions().OrderByDescending(v => v.ApiVersion))
        {
            app.UseOpenApi(settings =>
            {
                settings.DocumentName = apiVersionDescription.GroupName;
                settings.Path = $"/openapi/{apiVersionDescription.GroupName}.json";
                documentSettings?.Invoke(settings, apiVersionDescription);
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

    public static WebApplication MapFrameworkNswagOpenApi(
        this WebApplication app,
        Action<OpenApiDocumentMiddlewareSettings>? documentSettings = null,
        Action<SwaggerUiSettings>? uiSettings = null
    )
    {
        app.UseOpenApi(settings =>
        {
            settings.Path = "/openapi/{documentName}.json";
            documentSettings?.Invoke(settings);
        });

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
            In = OpenApiSecurityApiKeyLocation.Header,
            Type = OpenApiSecuritySchemeType.Http,
            Description = """
                JWT Authorization header using the Bearer scheme.
                Enter Token without any prefix.
                """,
        };
    }

    #endregion

    #region Primivites

    /// <summary>Adds Swagger mappings for specific custom types to ensure proper OpenAPI documentation generation.</summary>
    /// <param name="settings">The JsonSchemaGeneratorSettings instance to which mappings are added.</param>
    /// <remarks>
    /// The method adds Swagger mappings for the following types:
    /// <see cref="Money" />
    /// <see cref="Month" />
    /// <see cref="AccountId" />
    /// <see cref="UserId" />
    /// </remarks>
    public static void AddBuildingBlocksPrimitiveMappings(this JsonSchemaGeneratorSettings settings)
    {
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(Money),
                schema =>
                {
                    schema.Type = JsonObjectType.Number;
                    schema.Format = JsonFormatStrings.Decimal;
                    schema.Title = "Money";
                }
            )
        );
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(Money?),
                schema =>
                {
                    schema.Type = JsonObjectType.Number;
                    schema.Format = JsonFormatStrings.Decimal;
                    schema.IsNullableRaw = true;
                    schema.Title = "Nullable<Money>";
                }
            )
        );
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(Month),
                schema =>
                {
                    schema.Type = JsonObjectType.Integer;
                    schema.Format = JsonFormatStrings.Integer;
                    schema.Title = "Month";
                }
            )
        );
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(Month?),
                schema =>
                {
                    schema.Type = JsonObjectType.Integer;
                    schema.Format = JsonFormatStrings.Integer;
                    schema.IsNullableRaw = true;
                    schema.Title = "Nullable<Month>";
                }
            )
        );
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(AccountId),
                schema =>
                {
                    schema.Type = JsonObjectType.String;
                    schema.Format = "string";
                    schema.Title = "AccountId";
                }
            )
        );
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(UserId),
                schema =>
                {
                    schema.Type = JsonObjectType.String;
                    schema.Format = "string";
                    schema.Title = "UserId";
                }
            )
        );
    }

    #endregion

    #region Configurations

    private static void _ConfigureGeneratorSettings(
        AspNetCoreOpenApiDocumentGeneratorSettings settings,
        IServiceProvider serviceProvider
    )
    {
        // General Settings
        var productName = AssemblyInformation.Entry.Product?.Replace('.', ' ');
        settings.Title = productName is null ? "API" : productName.EnsureEndsWith(" API");
        settings.Description = SwaggerInformation.ResponsesDescription;
        settings.DefaultResponseReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;
        settings.GenerateOriginalParameterNames = true;
        settings.UseRouteNameAsOperationId = true;
        settings.SchemaSettings.UseXmlDocumentation = true;
        settings.SchemaSettings.GenerateEnumMappingDescription = true;
        settings.SchemaSettings.FlattenInheritanceHierarchy = true;
        settings.SchemaSettings.DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;
        settings.SchemaSettings.DefaultDictionaryValueReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;
        // Schema Processors
        settings.SchemaSettings.SchemaProcessors.Add(new FluentValidationSchemaProcessor(serviceProvider));
        settings.SchemaSettings.SchemaProcessors.Add(new NullabilityAsRequiredSchemaProcessor());
        // Operation Processors
        settings.OperationProcessors.Add(new ApiExtraInformationOperationProcessor());
        settings.OperationProcessors.Add(new UnauthorizedResponseOperationProcessor());
        settings.OperationProcessors.Add(new ForbiddenResponseOperationProcessor());
    }

    private static void _ConfigureGeneratorSettingsByFramework(
        AspNetCoreOpenApiDocumentGeneratorSettings settings,
        Action<FrameworkNswagOptions>? setupFrameworkAction
    )
    {
        var frameworkOptions = new FrameworkNswagOptions();
        setupFrameworkAction?.Invoke(frameworkOptions);

        if (frameworkOptions.AddBearerSecurity)
        {
            settings.AddSecurity(_BearerDefinitionName, [], _GetBearerSecurityDefinition());
            settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(_BearerDefinitionName));
        }

        if (frameworkOptions.AddPrimitiveMappings)
        {
            settings.SchemaSettings.AddBuildingBlocksPrimitiveMappings();
        }
    }

    #endregion
}
