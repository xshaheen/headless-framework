// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using FluentValidation;
using Headless.Api;
using Headless.Checks;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.OpenApi;

/// <summary>
/// Registers ASP.NET Core OpenAPI documents with the Headless transformers, and configures the shared Headless
/// enrichment options.
/// </summary>
[PublicAPI]
public static class SetupHeadlessOpenApi
{
    // Marks the options instances that already carry the transformers; the table does not keep them alive.
    private static readonly ConditionalWeakTable<OpenApiOptions, object> _EnrichedDocuments = [];

    /// <summary>The name of the document <see cref="AddHeadlessOpenApi"/> registers by default.</summary>
    public const string DefaultDocumentName = "v1";

    /// <summary>
    /// Registers one ASP.NET Core OpenAPI document with the Headless transformers applied, then
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="documentName">
    /// The document name. The native generator lowercases it; it is served at <c>MapOpenApi()</c>'s route with
    /// that name. Default is <see cref="DefaultDocumentName"/>.
    /// </param>
    /// <param name="configure">
    /// Optional callback for the native <see cref="OpenApiOptions"/>. It runs after the Headless transformers are
    /// added, so its transformers run after them and can override their output.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// Call it once per document, for example once per API version with a <c>ShouldInclude</c> predicate on
    /// <c>ApiDescription.GroupName</c>. Serve the documents with <c>app.MapOpenApi()</c>, which maps an endpoint, so
    /// it follows the authorization pipeline like any other endpoint.
    /// </remarks>
    public static IServiceCollection AddHeadlessOpenApi(
        this IServiceCollection services,
        string documentName = DefaultDocumentName,
        Action<OpenApiOptions>? configure = null
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(documentName);

        _AddOptions(services);
        services.AddOpenApi(
            documentName,
            options =>
            {
                options.AddHeadlessTransformers();
                configure?.Invoke(options);
            }
        );

        return services;
    }

    /// <summary>
    /// Registers one ASP.NET Core OpenAPI document per API surface, each with the Headless transformers applied and
    /// filtered to the surface's endpoints.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="documentNames">
    /// The surface documents to register. <see langword="null"/> infers every surface registered so far and closes
    /// surface registration. Explicit names must match <c>ApiSurfaceBuilder.OpenApi.DocumentName</c>.
    /// </param>
    /// <param name="configure">
    /// Optional per-surface callback for the native <see cref="OpenApiOptions"/>. It runs after the Headless
    /// transformers and the surface title, and its <c>ShouldInclude</c> predicate narrows the surface further.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// The surface filter runs before schema generation, so another surface's paths and schemas cannot leak into a
    /// document. It is independent of API Explorer version groups: narrow by <c>ApiDescription.GroupName</c> in
    /// <paramref name="configure"/> when one surface publishes several versions.
    /// </remarks>
    public static IServiceCollection AddHeadlessApiSurfaceDocuments(
        this IServiceCollection services,
        IEnumerable<string>? documentNames = null,
        Action<OpenApiOptions, ApiSurfaceDescriptor>? configure = null
    )
    {
        Argument.IsNotNull(services);
        documentNames ??= ApiSurfaceRegistration.InferDocumentNames(services);
        _AddOptions(services);

        foreach (var documentName in documentNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Argument.IsNotNullOrWhiteSpace(documentName);
            // The native generator lowercases registration keys and requested document names alike.
            var name = documentName.ToLowerInvariant();
            services.AddOpenApi(name);
            services
                .AddOptions<OpenApiOptions>(name)
                .Configure<ApiSurfaceRegistry>(
                    (options, registry) =>
                    {
                        var surface = registry.GetRequiredSurfaceForDocument(name);
                        // The native default ties document names to API Explorer version groups; surfaces are not groups.
                        options.ShouldInclude = _ => true;
                        options.AddHeadlessTransformers();
                        options.AddDocumentTransformer(
                            (document, _, _) =>
                            {
                                document.Info.Title = surface.OpenApi.Title;
                                return Task.CompletedTask;
                            }
                        );
                        configure?.Invoke(options, surface);
                        var include = options.ShouldInclude;
                        options.ShouldInclude = description =>
                            string.Equals(
                                description
                                    .GetEndpointMetadata()
                                    .OfType<IApiSurfaceMetadata>()
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

    /// <summary>Configures the Headless enrichment shared by every Headless OpenAPI document.</summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="setupAction">The callback that configures <see cref="HeadlessOpenApiOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// Callbacks compose in registration order and may run before or after the document registrations. The options
    /// are validated when the host starts.
    /// </remarks>
    public static IServiceCollection ConfigureHeadlessOpenApi(
        this IServiceCollection services,
        Action<HeadlessOpenApiOptions> setupAction
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        _AddDocumentServices(services);
        services.Configure<HeadlessOpenApiOptions, HeadlessOpenApiOptionsValidator>(setupAction);

        return services;
    }

    /// <summary>
    /// Configures the Headless enrichment shared by every Headless OpenAPI document with access to the application
    /// services.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="setupAction">The callback that configures <see cref="HeadlessOpenApiOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection ConfigureHeadlessOpenApi(
        this IServiceCollection services,
        Action<HeadlessOpenApiOptions, IServiceProvider> setupAction
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(setupAction);

        _AddDocumentServices(services);
        services.Configure<HeadlessOpenApiOptions, HeadlessOpenApiOptionsValidator>(setupAction);

        return services;
    }

    /// <summary>Adds the Headless document, operation, and schema transformers to one OpenAPI document.</summary>
    /// <param name="options">The native options of the document.</param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <remarks>
    /// <see cref="AddHeadlessOpenApi"/> and <see cref="AddHeadlessApiSurfaceDocuments"/> call this for you. Call it
    /// directly only for a document registered with the native <c>AddOpenApi(...)</c>. Call it before adding your own
    /// transformers: transformers run in registration order, so yours then see and can override the Headless output.
    /// It also inlines Headless primitive types instead of publishing each one as a component schema. Calling it again on
    /// the same options does nothing, so a callback that adds it to a document that already has it is harmless.
    /// </remarks>
    public static OpenApiOptions AddHeadlessTransformers(this OpenApiOptions options)
    {
        Argument.IsNotNull(options);

        if (!_EnrichedDocuments.TryAdd(options, new object()))
        {
            return options;
        }

        var createSchemaReferenceId = options.CreateSchemaReferenceId;
        // A primitive serializes as its underlying value, so a component per primitive would only add an alias.
        options.CreateSchemaReferenceId = typeInfo =>
            PrimitiveSchemaTransformer.IsPrimitive(typeInfo.Type) ? null : createSchemaReferenceId(typeInfo);

        // Schema transformers each walk the whole schema tree in turn, so a later one sees the earlier ones' output.
        options.AddSchemaTransformer(new PrimitiveSchemaTransformer());
        options.AddSchemaTransformer(new NumberSchemaTransformer());
        options.AddSchemaTransformer(new FluentValidationSchemaTransformer());
        options.AddSchemaTransformer(new RequiredSchemaTransformer());

        // The response transformers must precede ProblemDetails, which types every error response that exists then.
        options.AddOperationTransformer(new OperationIdTransformer());
        options.AddOperationTransformer(new ApiExtraInformationTransformer());
        options.AddOperationTransformer(new CamelCaseQueryParameterTransformer());
        options.AddOperationTransformer(new UnauthorizedResponseTransformer());
        options.AddOperationTransformer(new ForbiddenResponseTransformer());
        options.AddOperationTransformer(new IfMatchTransformer());
        options.AddOperationTransformer(new RateLimitingResponseTransformer());
        options.AddOperationTransformer(new EntityTagResponseTransformer());
        options.AddOperationTransformer(new HttpContractTransformer());
        // After the feature contracts, so a feature's own description of a 400 or 413 wins over the generic one.
        options.AddOperationTransformer(new RequestReadingResponseTransformer());
        options.AddOperationTransformer(new FormFileValidationTransformer());
        options.AddOperationTransformer(new SecurityRequirementTransformer());
        options.AddOperationTransformer(new ProblemDetailsTransformer());
        options.AddOperationTransformer(new TenantRequiredExampleTransformer());
        // After the problem types, so it can tell a response the operation already documents from one only the tenant
        // rejection produces.
        options.AddOperationTransformer(new TenantResolutionTransformer());

        options.AddDocumentTransformer(new DocumentInfoTransformer());
        options.AddDocumentTransformer(new SecuritySchemesTransformer());
        options.AddDocumentTransformer(new TenantServersTransformer());
        options.AddDocumentTransformer(new OperationIdDeduplicationTransformer());
        options.AddDocumentTransformer(new ProblemDetailsComponentsTransformer());

        return options;
    }

    /// <summary>Whether <see cref="AddHeadlessTransformers"/> ran on <paramref name="options"/>.</summary>
    internal static bool HasHeadlessTransformers(OpenApiOptions options)
    {
        return _EnrichedDocuments.TryGetValue(options, out _);
    }

    private static void _AddOptions(IServiceCollection services)
    {
        _AddDocumentServices(services);

        if (services.Any(service => service.ServiceType == typeof(IValidator<HeadlessOpenApiOptions>)))
        {
            return;
        }

        services.AddOptions<HeadlessOpenApiOptions, HeadlessOpenApiOptionsValidator>();
    }

    // Registered apart from the options, because ConfigureHeadlessOpenApi registers the validator _AddOptions checks
    // for, and it is the only Headless call an app that builds its documents with the native AddOpenApi() makes.
    private static void _AddDocumentServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<OpenApiOptions>, EnvironmentRestrictionFilter>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IApiDescriptionProvider, EndpointMetadataDescriptionProvider>()
        );
    }
}
