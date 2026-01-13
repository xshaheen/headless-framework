// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning.ApiExplorer;
using Framework.Api.ApiExplorer;
using Framework.Api.OperationProcessors;
using Framework.Api.SchemaProcessors;
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

namespace Framework.Api;

[PublicAPI]
public static class NswagSetup
{
    #region Add

    public static IServiceCollection AddHeadlessNswagOpenApi(
        this IServiceCollection services,
        Action<HeadlessNswagOptions>? setupFrameworkAction = null,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings>? setupGeneratorActions = null
    )
    {
        var frameworkOptions = _BuildOptions(setupFrameworkAction);

        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                _ConfigureGeneratorSettings(settings, serviceProvider, frameworkOptions);
                setupGeneratorActions?.Invoke(settings);
                _ConfigureGeneratorSettingsByFramework(settings, frameworkOptions);
            }
        );

        return services;
    }

    public static IServiceCollection AddHeadlessNswagOpenApi(
        this IServiceCollection services,
        Action<HeadlessNswagOptions>? setupFrameworkAction,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider> setupGeneratorActions
    )
    {
        var frameworkOptions = _BuildOptions(setupFrameworkAction);

        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                _ConfigureGeneratorSettings(settings, serviceProvider, frameworkOptions);
                setupGeneratorActions?.Invoke(settings, serviceProvider);
                _ConfigureGeneratorSettingsByFramework(settings, frameworkOptions);
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

    private const string _BearerDefinitionName = "Bearer";

    private static OpenApiSecurityScheme _GetBearerSecurityDefinition()
    {
        return new OpenApiSecurityScheme
        {
            Scheme = "bearer",
            BearerFormat = "JWT",
            Type = OpenApiSecuritySchemeType.Http,
            Description = """
                JWT Authorization header using the Bearer scheme.
                Enter Token without any prefix.
                """,
        };
    }

    #endregion

    #region ApiKey

    private const string _ApiKeyDefinitionName = "ApiKey";

    private static OpenApiSecurityScheme _GetApiKeySecurityDefinition(string headerName)
    {
        return new OpenApiSecurityScheme
        {
            Name = headerName,
            Type = OpenApiSecuritySchemeType.ApiKey,
            In = OpenApiSecurityApiKeyLocation.Header,
            Description = $"API Key authentication using the {headerName} header.",
        };
    }

    #endregion

    #region Primitives

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

    private static HeadlessNswagOptions _BuildOptions(Action<HeadlessNswagOptions>? setupFrameworkAction)
    {
        var options = new HeadlessNswagOptions();
        setupFrameworkAction?.Invoke(options);
        return options;
    }

    private static void _ConfigureGeneratorSettings(
        AspNetCoreOpenApiDocumentGeneratorSettings settings,
        IServiceProvider serviceProvider,
        HeadlessNswagOptions frameworkOptions
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
        settings.SchemaSettings.SchemaProcessors.Add(new FluentValidationSchemaProcessor(serviceProvider, frameworkOptions));
        settings.SchemaSettings.SchemaProcessors.Add(new NullabilityAsRequiredSchemaProcessor());
        // Operation Processors
        settings.OperationProcessors.Add(new ApiExtraInformationOperationProcessor());
        settings.OperationProcessors.Add(new UnauthorizedResponseOperationProcessor());
        settings.OperationProcessors.Add(new ForbiddenResponseOperationProcessor());
        settings.OperationProcessors.Add(new ProblemDetailsOperationProcessor());
    }

    private static void _ConfigureGeneratorSettingsByFramework(
        AspNetCoreOpenApiDocumentGeneratorSettings settings,
        HeadlessNswagOptions frameworkOptions
    )
    {
        if (frameworkOptions.AddBearerSecurity)
        {
            settings.AddSecurity(_BearerDefinitionName, [], _GetBearerSecurityDefinition());
            settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(_BearerDefinitionName));
        }

        if (frameworkOptions.AddApiKeySecurity)
        {
            settings.AddSecurity(
                _ApiKeyDefinitionName,
                [],
                _GetApiKeySecurityDefinition(frameworkOptions.ApiKeyHeaderName)
            );
            settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(_ApiKeyDefinitionName));
        }

        if (frameworkOptions.AddPrimitiveMappings)
        {
            settings.SchemaSettings.AddBuildingBlocksPrimitiveMappings();
        }
    }

    #endregion
}
