// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Applies root-validator member paths at their use sites, without modifying the nested components.</summary>
internal sealed class FluentValidationNestedRules(
    OpenApiDocument document,
    JsonSerializerOptions jsonOptions,
    IServiceProvider services
)
{
    private readonly ILogger _logger =
        (ILogger?)services.GetService<ILoggerFactory>()?.CreateLogger<FluentValidationSchemaTransformer>()
        ?? NullLogger.Instance;

    public void Apply(OpenApiSchema schema, JsonTypeInfo typeInfo)
    {
        if (services.GetFluentValidator(typeInfo.Type, _logger) is not { } validator)
        {
            return;
        }

        try
        {
            _Apply(schema, typeInfo, validator.GetValidatorsIncludingIncludes());
        }
        catch (Exception exception)
        {
            _logger.LogIncludeRulesFailed(exception, typeInfo.Type.Name);

            if (services.GetService<IOptions<HeadlessOpenApiOptions>>()?.Value.ThrowOnSchemaProcessingError == true)
            {
                throw;
            }
        }
    }

    private void _Apply(OpenApiSchema schema, JsonTypeInfo typeInfo, List<IValidator> validators)
    {
        var names = validators
            .SelectMany(validator => validator as IEnumerable<IValidationRule> ?? [])
            .Where(rule => rule.HasNoCondition() && !rule.IsCollectionRule())
            .Select(rule => rule.PropertyName)
            .OfType<string>()
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var overlays = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
        var applier = new FluentValidationSchemaTransformer.RuleApplier(services, _logger);

        foreach (var name in names)
        {
            var rules = validators
                .SelectMany(validator => validator.GetValidatorsByPropertyNameIgnoreCase(name))
                .Where(rule => !rule.IsCollectionRule)
                .ToList();

            if (rules.Count == 0 || _ResolvePath(schema, typeInfo, name) is not { } path)
            {
                continue;
            }

            if (!overlays.TryGetValue(path[0].Key, out var overlay))
            {
                overlays[path[0].Key] = overlay = new OpenApiSchema();
            }

            for (var i = 1; i < path.Count; i++)
            {
                overlay.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

                if (!overlay.Properties.TryGetValue(path[i].Key, out var child))
                {
                    overlay.Properties[path[i].Key] = child = new OpenApiSchema();
                }

                if (i == path.Count - 1)
                {
                    // The type lets NotNull/NotEmpty exclude a nullable leaf and distinguish an array from a string.
                    // Bounds, descriptions, and object members stay in the base schema rather than being duplicated.
                    ((OpenApiSchema)child).Type ??= _ConstraintType(path[i].Schema);
                    applier.ApplyToProperty(overlay, path[i].Key, rules, typeInfo.Type.Name);
                }
                else
                {
                    overlay = (OpenApiSchema)child;
                }
            }
        }

        foreach (var (key, overlay) in overlays)
        {
            schema.Properties![key] = _WithOverlay(schema.Properties[key], overlay);
        }
    }

    private List<(string Key, IOpenApiSchema Schema)>? _ResolvePath(
        IOpenApiSchema schema,
        JsonTypeInfo typeInfo,
        string name
    )
    {
        var path = new List<(string, IOpenApiSchema)>();
        var segments = name.Split('.');

        for (var i = 0; i < segments.Length; i++)
        {
            var property = typeInfo.Properties.FirstOrDefault(property =>
                string.Equals(
                    property.AttributeProvider is MemberInfo member ? member.Name : property.Name,
                    segments[i],
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (property is null || _FindProperty(schema, property.Name, []) is not { } child)
            {
                return null;
            }

            path.Add((property.Name, child));
            schema = child;

            if (i < segments.Length - 1)
            {
                typeInfo = jsonOptions.GetTypeInfo(property.PropertyType);

                if (typeInfo.Kind != JsonTypeInfoKind.Object)
                {
                    return null;
                }
            }
        }

        return path;
    }

    private IOpenApiSchema? _FindProperty(IOpenApiSchema schema, string key, HashSet<IOpenApiSchema> seen)
    {
        if (!seen.Add(schema))
        {
            return null;
        }

        if (schema is OpenApiSchemaReference { Reference.Id: { } id })
        {
            return document.Components?.Schemas?.TryGetValue(id, out var component) == true
                ? _FindProperty(component, key, seen)
                : null;
        }

        if (schema.Properties?.TryGetValue(key, out var property) == true)
        {
            return property;
        }

        foreach (var alternative in (schema.OneOf ?? []).Concat(schema.AllOf ?? []))
        {
            if (_FindProperty(alternative, key, seen) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static JsonSchemaType? _ConstraintType(IOpenApiSchema schema)
    {
        if (schema.Type is { } type)
        {
            return type;
        }

        JsonSchemaType? combined = null;

        foreach (var alternative in schema.OneOf ?? schema.AnyOf ?? schema.AllOf ?? [])
        {
            if (_ConstraintType(alternative) is { } alternativeType)
            {
                combined = combined is { } current ? current | alternativeType : alternativeType;
            }
        }

        return combined;
    }

    private static OpenApiSchema _WithOverlay(IOpenApiSchema schema, OpenApiSchema overlay)
    {
        if (
            schema is OpenApiSchema { OneOf: { } alternatives } nullable
            && alternatives.Any(alternative => alternative.Type == JsonSchemaType.Null)
        )
        {
            var copy = (OpenApiSchema)nullable.CreateShallowCopy();
            copy.OneOf =
            [
                .. alternatives.Select(alternative =>
                    alternative.Type == JsonSchemaType.Null ? alternative : _WithOverlay(alternative, overlay)
                ),
            ];

            return copy;
        }

        return new OpenApiSchema { AllOf = [schema, overlay] };
    }
}
