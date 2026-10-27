// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Gives the parts of an MVC form body the constraints and required-ness a JSON body gets: the
/// <see cref="FluentValidationRule"/> set, and a <c>"required"</c> entry for every part whose property is non-nullable.
/// </summary>
/// <remarks>
/// <para>
/// A JSON body is a component, so the schema transformers constrain it member by member. MVC instead describes each
/// leaf property of a <c>[FromForm]</c> model as its own parameter, and the generator builds the multipart schema from
/// them during operation generation, where no schema transformer sees it: every part came out optional, so a client
/// generated from it typed a required file as <c>Avatar?</c>. <see cref="FormPart"/> finds each part's rules, which
/// this applies to the multipart schema as the JSON transformers apply them to a component.
/// </para>
/// <para>
/// A part below an object that may be absent (<c>Name.First</c> with a nullable <c>Name</c>) is never required,
/// because a flat form cannot say "required when its parent is sent". A Minimal API form model is published as a
/// component, which the schema transformers already constrain, so its body is left alone.
/// </para>
/// </remarks>
internal sealed class FormPartValidationTransformer : IOpenApiOperationTransformer
{
    private static readonly string[] _FormMediaTypes = ["multipart/form-data", "application/x-www-form-urlencoded"];

    private ILogger? _logger;

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (operation.RequestBody?.Content is not { } content)
        {
            return Task.CompletedTask;
        }

        // Every form media type shares one schema, and several form parameters publish one object each in allOf.
        var schemas = new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance);

        foreach (var mediaType in _FormMediaTypes)
        {
            if (content.TryGetValue(mediaType, out var media) && media.Schema is OpenApiSchema schema)
            {
                schemas.Add(schema);
                schemas.UnionWith(schema.AllOf?.OfType<OpenApiSchema>() ?? []);
            }
        }

        if (schemas.Count == 0)
        {
            return Task.CompletedTask;
        }

        var services = context.ApplicationServices;
        var logger = _GetLogger(services);
        FluentValidationSchemaTransformer.RuleApplier? applier = null;

        foreach (var parameter in context.Description.ParameterDescriptions)
        {
            FormPart? part = null;

            foreach (var schema in schemas)
            {
                if (schema.Properties?.ContainsKey(parameter.Name) != true)
                {
                    continue;
                }

                part ??= FormPart.Resolve(parameter, services, logger);

                if (part is null)
                {
                    break;
                }

                applier ??= new FluentValidationSchemaTransformer.RuleApplier(services, logger);
                applier.ApplyToProperty(schema, parameter.Name, part.Validators, part.ContainerName);

                if (part.HasOptionalAncestor)
                {
                    schema.Required?.Remove(parameter.Name);
                }
                else if (part.IsNonNullable)
                {
                    (schema.Required ??= new HashSet<string>(StringComparer.Ordinal)).Add(parameter.Name);
                }
            }
        }

        return Task.CompletedTask;
    }

    // One logger per transformer, which belongs to one application's document; a race only creates a spare logger.
    private ILogger _GetLogger(IServiceProvider services)
    {
        return _logger ??=
            (ILogger?)services.GetService<ILoggerFactory>()?.CreateLogger<FormPartValidationTransformer>()
            ?? NullLogger.Instance;
    }
}
