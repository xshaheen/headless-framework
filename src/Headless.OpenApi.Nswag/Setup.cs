// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning.ApiExplorer;
using Headless.Api.ApiExplorer;
using Headless.OpenApi.Nswag.OperationProcessors;
using Headless.OpenApi.Nswag.SchemaProcessors;
using Headless.Reflection;
using Headless.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Generation.TypeMappers;
using NSwag;
using NSwag.AspNetCore;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors.Security;
using AccountId = Headless.Primitives.AccountId;
using MoneyAmount = Headless.Primitives.MoneyAmount;
using Month = Headless.Primitives.Month;
using UserId = Headless.Primitives.UserId;

namespace Headless.OpenApi.Nswag;

/// <summary>
/// Registration and middleware helpers for NSwag-based OpenAPI document generation and Swagger UI.
/// </summary>
[PublicAPI]
public static class SetupNswag
{
    #region Add

    /// <summary>
    /// Registers NSwag OpenAPI document generation with Headless defaults and an optional generator settings callback.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="setupHeadlessAction">
    /// Optional callback to configure <see cref="HeadlessNswagOptions"/> (security schemes, primitive
    /// mappings, error-on-schema-failure). When <see langword="null"/>, defaults are used.
    /// </param>
    /// <param name="setupGeneratorActions">
    /// Optional callback invoked after Headless base settings are applied but before security schemes and
    /// primitive mappings, allowing callers to override individual generator settings.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddNswagOpenApi(
        this IServiceCollection services,
        Action<HeadlessNswagOptions>? setupHeadlessAction = null,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings>? setupGeneratorActions = null
    )
    {
        var headlessOptions = _BuildOptions(setupHeadlessAction);

        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                _ConfigureGeneratorSettings(settings, serviceProvider, headlessOptions);
                setupGeneratorActions?.Invoke(settings);
                _ConfigureHeadlessGeneratorSettings(settings, headlessOptions);
            }
        );

        return services;
    }

    /// <summary>
    /// Registers NSwag OpenAPI document generation with Headless defaults and a service-provider-aware generator
    /// settings callback.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="setupHeadlessAction">
    /// Optional callback to configure <see cref="HeadlessNswagOptions"/>. When <see langword="null"/>, defaults
    /// are used.
    /// </param>
    /// <param name="setupGeneratorActions">
    /// Optional callback that receives both the generator settings and the <see cref="IServiceProvider"/>, invoked
    /// after Headless base settings are applied but before security schemes and primitive mappings.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddNswagOpenApi(
        this IServiceCollection services,
        Action<HeadlessNswagOptions>? setupHeadlessAction,
        Action<AspNetCoreOpenApiDocumentGeneratorSettings, IServiceProvider>? setupGeneratorActions
    )
    {
        var headlessOptions = _BuildOptions(setupHeadlessAction);

        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                _ConfigureGeneratorSettings(settings, serviceProvider, headlessOptions);
                setupGeneratorActions?.Invoke(settings, serviceProvider);
                _ConfigureHeadlessGeneratorSettings(settings, headlessOptions);
            }
        );

        return services;
    }

    #endregion

    #region Map

    /// <summary>
    /// Mounts one OpenAPI JSON endpoint per API version and a single Swagger UI at <c>/swagger</c>.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <param name="documentSettings">
    /// Optional per-version callback invoked for each <c>ApiVersionDescription</c> to customise the
    /// <c>UseOpenApi</c> middleware settings (for example, to add custom headers or alter the path).
    /// </param>
    /// <param name="uiSettings">
    /// Optional callback to further customise the Swagger UI (for example, to change the page title or
    /// inject custom CSS). Headless defaults: authorization persistence enabled, try-it-out enabled,
    /// tags sorted alphabetically, operations collapsed.
    /// </param>
    /// <returns>The same <paramref name="app"/> instance for chaining.</returns>
    /// <remarks>
    /// Each version is served at <c>/openapi/{groupName}.json</c> in descending version order.
    /// The UI document selector pattern is <c>/openapi/{documentName}.json</c>, which matches all
    /// version endpoints automatically.
    /// </remarks>
    public static WebApplication MapNswagOpenApiVersions(
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

    /// <summary>
    /// Mounts an OpenAPI JSON endpoint at <c>/openapi/{documentName}.json</c> and a Swagger UI at <c>/swagger</c>
    /// for a single-document (non-versioned) setup.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <param name="documentSettings">
    /// Optional callback to customise the <c>UseOpenApi</c> middleware settings.
    /// </param>
    /// <param name="uiSettings">
    /// Optional callback to further customise the Swagger UI. Headless defaults: authorization persistence
    /// enabled, try-it-out enabled, tags sorted alphabetically, operations collapsed.
    /// </param>
    /// <returns>The same <paramref name="app"/> instance for chaining.</returns>
    public static WebApplication MapNswagOpenApi(
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

    /// <summary>
    /// Registers NJsonSchema type mappers for the built-in Headless primitive value types so they are
    /// represented with correct JSON schema types and formats in the generated OpenAPI document.
    /// </summary>
    /// <param name="settings">The <c>JsonSchemaGeneratorSettings</c> to add the mappers to.</param>
    /// <remarks>
    /// Mapped types and their resulting schema shapes:
    /// <list type="bullet">
    ///   <item><description><c>MoneyAmount</c> / <c>MoneyAmount?</c> — <c>number</c> (decimal format)</description></item>
    ///   <item><description><c>Month</c> / <c>Month?</c> — <c>integer</c></description></item>
    ///   <item><description><c>AccountId</c> — <see langword="string"/></description></item>
    ///   <item><description><c>UserId</c> — <see langword="string"/></description></item>
    /// </list>
    /// This method is called automatically when <see cref="HeadlessNswagOptions.AddPrimitiveMappings"/> is
    /// <see langword="true"/> (the default). Call it directly only when you manage your own
    /// <c>JsonSchemaGeneratorSettings</c> outside of <c>AddNswagOpenApi</c>.
    /// </remarks>
    public static void AddBuildingBlocksPrimitiveMappings(this JsonSchemaGeneratorSettings settings)
    {
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(MoneyAmount),
                schema =>
                {
                    schema.Type = JsonObjectType.Number;
                    schema.Format = JsonFormatStrings.Decimal;
                    schema.Title = "MoneyAmount";
                }
            )
        );
        settings.TypeMappers.Add(
            new PrimitiveTypeMapper(
                typeof(MoneyAmount?),
                schema =>
                {
                    schema.Type = JsonObjectType.Number;
                    schema.Format = JsonFormatStrings.Decimal;
                    schema.IsNullableRaw = true;
                    schema.Title = "Nullable<MoneyAmount>";
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

    private static HeadlessNswagOptions _BuildOptions(Action<HeadlessNswagOptions>? setupAction)
    {
        var options = new HeadlessNswagOptions();
        setupAction?.Invoke(options);
        return options;
    }

    private static void _ConfigureGeneratorSettings(
        AspNetCoreOpenApiDocumentGeneratorSettings settings,
        IServiceProvider serviceProvider,
        HeadlessNswagOptions headlessOptions
    )
    {
        // General Settings
        var productName = AssemblyInformation.Entry?.Product?.Replace('.', ' ');
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
        settings.SchemaSettings.SchemaProcessors.Add(
            new FluentValidationSchemaProcessor(serviceProvider, headlessOptions)
        );
        settings.SchemaSettings.SchemaProcessors.Add(new GenericNullabilitySchemaProcessor());
        settings.SchemaSettings.SchemaProcessors.Add(new NullabilityAsRequiredSchemaProcessor());
        // Operation Processors
        settings.OperationProcessors.Add(new ApiExtraInformationOperationProcessor());
        settings.OperationProcessors.Add(new CamelCaseQueryParameterOperationProcessor());
        settings.OperationProcessors.Add(new UnauthorizedResponseOperationProcessor());
        settings.OperationProcessors.Add(new ForbiddenResponseOperationProcessor());
        settings.OperationProcessors.Add(new ProblemDetailsOperationProcessor());
    }

    private static void _ConfigureHeadlessGeneratorSettings(
        AspNetCoreOpenApiDocumentGeneratorSettings settings,
        HeadlessNswagOptions headlessOptions
    )
    {
        if (headlessOptions.AddBearerSecurity)
        {
            settings.AddSecurity(_BearerDefinitionName, [], _GetBearerSecurityDefinition());
            settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(_BearerDefinitionName));
        }

        if (headlessOptions.AddApiKeySecurity)
        {
            settings.AddSecurity(
                _ApiKeyDefinitionName,
                [],
                _GetApiKeySecurityDefinition(headlessOptions.ApiKeyHeaderName)
            );
            settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(_ApiKeyDefinitionName));
        }

        if (headlessOptions.AddPrimitiveMappings)
        {
            settings.SchemaSettings.AddBuildingBlocksPrimitiveMappings();
        }
    }

    #endregion
}
