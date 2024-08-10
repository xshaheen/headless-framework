using Framework.Api.Core.ApiExplorer;
using Framework.Api.Swagger.Nswag.OperationProcessors;
using Framework.Api.Swagger.Nswag.SchemaProcessors;
using Framework.BuildingBlocks.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema.Generation;
using NSwag;
using NSwag.Generation.Processors.Security;
using Scalar.AspNetCore;
using Zad.Framework.BuildingBlocks.Primitives.Converters.Extensions;

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
                settings.SchemaSettings.AddSwaggerPrimitiveMappings();
                settings.SchemaSettings.SchemaProcessors.Add(new FluentValidationSchemaProcessor(serviceProvider));
                settings.SchemaSettings.SchemaProcessors.Add(new NullabilityAsRequiredSchemaProcessor());
                settings.OperationProcessors.Add(new ApiExtraInformationOperationProcessor());
                settings.OperationProcessors.Add(new UnauthorizedResponseOperationProcessor());
                settings.OperationProcessors.Add(new ForbiddenResponseOperationProcessor());

                settings.AddSecurity(
                    "JWT",
                    [],
                    new OpenApiSecurityScheme
                    {
                        Type = OpenApiSecuritySchemeType.ApiKey,
                        Name = "Authorization",
                        In = OpenApiSecurityApiKeyLocation.Header,
                        Description =
                            "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    }
                );

                settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("JWT"));
            }
        );

        return services;
    }

    public static WebApplication UseFrameworkNswagSwagger(this WebApplication app)
    {
        app.MapScalarApiReference(b =>
        {
            b.DarkMode = true;
            b.EndpointPathPrefix = "/scalar";
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
}
