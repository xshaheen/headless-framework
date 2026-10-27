// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Headless.Api;
using Headless.FluentValidation;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Describes the FluentValidation file rules of a form request body: the accepted content types of a file part become
/// its multipart encoding <c>contentType</c>, and the accepted types and size bounds are added to the part's
/// description.
/// </summary>
/// <remarks>
/// <para>
/// It reads the rules through <see cref="IFileContentTypeValidator"/> and <see cref="IFileSizeValidator"/>, from
/// the validator of the form model a Minimal API binds with <c>[FromForm]</c>, or of the type that declares a property
/// MVC binds from the form. Several rules on one part narrow each other: the types are intersected, and the tightest
/// bounds win. Conditional rules are skipped, as they are for schemas.
/// </para>
/// <para>
/// This is an operation transformer rather than a <see cref="FluentValidationRule"/>, because the encoding belongs to the
/// request body, which a schema transformer never sees, and because a file part's schema is the shared
/// <c>IFormFile</c> component, which a per-property constraint must not change. OpenAPI has no keyword for the size of
/// a binary part (<c>maxLength</c> counts characters), so the bounds are only described.
/// </para>
/// </remarks>
internal sealed class FormFileValidationTransformer : IOpenApiOperationTransformer
{
    private const string _MultipartFormData = "multipart/form-data";

    private ILogger? _logger;

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        // FormFileRequestBodyTransformer runs first and publishes every body with a file part as multipart only.
        if (
            operation.RequestBody?.Content is not { } content
            || !content.TryGetValue(_MultipartFormData, out var mediaType)
            || _Resolve(context.Document, mediaType.Schema) is not { Properties: { Count: > 0 } properties }
        )
        {
            return Task.CompletedTask;
        }

        var rules = _GetFileRules(context);

        if (rules.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var key in properties.Keys.ToArray())
        {
            if (!rules.TryGetValue(key, out var rule))
            {
                continue;
            }

            _Describe(properties, key, rule.Describe());

            if (rule.ContentTypes is { Count: > 0 } contentTypes)
            {
                var encodings = mediaType.Encoding ??= new Dictionary<string, OpenApiEncoding>(StringComparer.Ordinal);

                if (!encodings.TryGetValue(key, out var encoding))
                {
                    encodings[key] = encoding = new OpenApiEncoding();
                }

                // OpenAPI reads a comma-separated list of media types here.
                encoding.ContentType = string.Join(", ", contentTypes);
            }
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, FileRule> _GetFileRules(OpenApiOperationTransformerContext context)
    {
        var services = context.ApplicationServices;
        var rules = new Dictionary<string, FileRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in context.Description.ParameterDescriptions)
        {
            if (parameter.Source != BindingSource.Form && parameter.Source != BindingSource.FormFile)
            {
                continue;
            }

            // MVC describes each form property of a model as its own parameter, named after the property.
            if (
                parameter.ModelMetadata is
                { MetadataKind: ModelMetadataKind.Property, ContainerType: { } containerType, PropertyName: { } name }
            )
            {
                if (services.GetFluentValidator(containerType, _GetLogger(services)) is { } container)
                {
                    _Collect(rules, parameter.Name, container.GetValidatorsByPropertyNameIgnoreCase(name));
                }

                continue;
            }

            // A Minimal API [FromForm] model is one parameter whose properties are the parts.
            if (parameter.Type is { } type && services.GetFluentValidator(type, _GetLogger(services)) is { } validator)
            {
                var names = (validator as IEnumerable<IValidationRule>)
                    .EmptyIfNull()
                    .Select(rule => rule.PropertyName)
                    .OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var memberName in names)
                {
                    _Collect(rules, memberName, validator.GetValidatorsByPropertyNameIgnoreCase(memberName));
                }
            }
        }

        return rules;
    }

    private static void _Collect(
        Dictionary<string, FileRule> rules,
        string key,
        IEnumerable<(IPropertyValidator Validator, bool IsCollectionRule)> validators
    )
    {
        foreach (var (validator, _) in validators)
        {
            if (validator is not (IFileContentTypeValidator or IFileSizeValidator))
            {
                continue;
            }

            if (!rules.TryGetValue(key, out var rule))
            {
                rules[key] = rule = new FileRule();
            }

            rule.Add(validator);
        }
    }

    private static OpenApiSchema? _Resolve(OpenApiDocument? document, IOpenApiSchema? schema)
    {
        return schema switch
        {
            OpenApiSchema concrete => concrete,
            OpenApiSchemaReference reference when reference.Reference.Id is { } id =>
                document?.Components?.Schemas?.TryGetValue(id, out var component) == true
                    ? component as OpenApiSchema
                    : null,
            _ => null,
        };
    }

    // The part's own node carries the text: a reference takes a sibling description in OpenAPI 3.1, so the shared
    // IFormFile component stays unchanged. The same model can back several operations, so the text is added once.
    private static void _Describe(IDictionary<string, IOpenApiSchema> properties, string key, string text)
    {
        var property = properties[key];
        var current = property.Description;

        if (current?.Contains(text, StringComparison.Ordinal) == true)
        {
            return;
        }

        var description = string.IsNullOrWhiteSpace(current) ? text : current + " " + text;

        switch (property)
        {
            case OpenApiSchemaReference reference:
                reference.Description = description;
                break;
            case OpenApiSchema schema:
                schema.Description = description;
                break;
        }
    }

    private ILogger _GetLogger(IServiceProvider services)
    {
        return _logger ??=
            (ILogger?)services.GetService<ILoggerFactory>()?.CreateLogger<FormFileValidationTransformer>()
            ?? NullLogger.Instance;
    }

    private sealed class FileRule
    {
        private List<string>? _contentTypes;

        public IReadOnlyList<string>? ContentTypes => _contentTypes;

        public long? MinBytes { get; private set; }

        public long? MaxBytes { get; private set; }

        public void Add(IPropertyValidator validator)
        {
            switch (validator)
            {
                case IFileContentTypeValidator contentTypes:
                    // Every rule must pass, so a second list narrows the first.
                    _contentTypes = _contentTypes is null
                        ? [.. contentTypes.ContentTypes.Distinct(StringComparer.OrdinalIgnoreCase)]
                        : [.. _contentTypes.Intersect(contentTypes.ContentTypes, StringComparer.OrdinalIgnoreCase)];
                    break;
                case IFileSizeValidator size:
                    if (size.MinBytes is { } min)
                    {
                        MinBytes = MinBytes is { } current ? Math.Max(current, min) : min;
                    }

                    if (size.MaxBytes is { } max)
                    {
                        MaxBytes = MaxBytes is { } current ? Math.Min(current, max) : max;
                    }

                    break;
            }
        }

        public string Describe()
        {
            var parts = new List<string>(2);

            if (_contentTypes is { Count: > 0 })
            {
                parts.Add($"Accepted content types: {string.Join(", ", _contentTypes)}.");
            }

            var size = (MinBytes, MaxBytes) switch
            {
                ({ } min, { } max) => $"Size: {_Bytes(min)} to {_Bytes(max)} bytes.",
                (1, null) => "Must not be empty.",
                ({ } min, null) => $"Size: at least {_Bytes(min)} bytes.",
                (null, { } max) => $"Size: at most {_Bytes(max)} bytes.",
                _ => null,
            };

            if (size is not null)
            {
                parts.Add(size);
            }

            return string.Join(' ', parts);
        }

        private static string _Bytes(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
