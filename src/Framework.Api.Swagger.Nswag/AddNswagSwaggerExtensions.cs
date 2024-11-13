// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.ApiExplorer;
using Framework.Api.Swagger.Nswag.Extensions;
using Framework.Api.Swagger.Nswag.OperationProcessors;
using Framework.Api.Swagger.Nswag.SchemaProcessors;
using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.BuildingBlocks.Helpers.Reflection;
using Framework.Kernel.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Generation.TypeMappers;
using NSwag;
using NSwag.Generation.Processors.Security;
using Scalar.AspNetCore;

namespace Framework.Api.Swagger.Nswag;

[PublicAPI]
public static class AddNswagSwaggerExtensions
{
    // TODO: Add options to configure Nswag Swagger

    /*
     * TODO: Problems with Nswag:
     * - Generic types of T can't detect nullability of T (e.g. T?) like DataEnvelope<T>
     * - query parameters should be camelCase
     * - form data parameters should be camelCase
     */

    public static IServiceCollection AddFrameworkNswagSwagger(this IServiceCollection services)
    {
        services.AddOpenApiDocument(
            (settings, serviceProvider) =>
            {
                var productName = AssemblyInformation.Entry.Product?.Replace('.', ' ');

                settings.DocumentName = "v1";
                settings.Version = "1.0.0";
                settings.Title = productName is null ? "API" : productName + " API";
                settings.Description = SwaggerInformation.ResponsesDescription;
                settings.DefaultResponseReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;
                settings.GenerateOriginalParameterNames = true;
                settings.UseRouteNameAsOperationId = true;
                settings.SchemaSettings.UseXmlDocumentation = true;
                settings.SchemaSettings.GenerateEnumMappingDescription = true;
                settings.SchemaSettings.FlattenInheritanceHierarchy = true;
                settings.SchemaSettings.DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;
                settings.SchemaSettings.DefaultDictionaryValueReferenceTypeNullHandling =
                    ReferenceTypeNullHandling.NotNull;

                settings.SchemaSettings.SchemaProcessors.Add(new FluentValidationSchemaProcessor(serviceProvider));
                settings.SchemaSettings.SchemaProcessors.Add(new NullabilityAsRequiredSchemaProcessor());

                settings.OperationProcessors.Add(new ApiExtraInformationOperationProcessor());
                settings.OperationProcessors.Add(new UnauthorizedResponseOperationProcessor());
                settings.OperationProcessors.Add(new ForbiddenResponseOperationProcessor());

                settings.AddSecurity(_BearerDefinitionName, [], _GetBearerSecurityDefinition());
                settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(_BearerDefinitionName));

                settings.SchemaSettings.AddBuildingBlocksPrimitiveMappings();
                settings.SchemaSettings.AddAllPrimitivesSwaggerMappings();
            }
        );

        return services;
    }

    public static WebApplication UseFrameworkNswagSwagger(this WebApplication app)
    {
        app.MapScalarApiReference(b =>
        {
            b.DarkMode = true;
            b.EndpointPathPrefix = "/scalar/{documentName}";
        });

        app.UseSwaggerUi(config =>
        {
            config.Path = "/swagger";
            config.DocumentPath = "/openapi/{documentName}.json";
            config.PersistAuthorization = true;
            config.EnableTryItOut = true;
            config.TagsSorter = "alpha";
            config.DocExpansion = "none";
        });

        foreach (var apiVersionDescription in app.DescribeApiVersions().OrderByDescending(v => v.ApiVersion))
        {
            app.UseOpenApi(settings =>
            {
                settings.DocumentName = apiVersionDescription.GroupName;
                settings.Path = $"/openapi/{apiVersionDescription.GroupName}.json";
            });
        }

        return app;
    }

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
}
