// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds XML documentation comments to the document: a type's summary to its schema, a property's summary (or a
/// positional record's <c>param</c>) to the property, and an action's summary, remarks, and parameter comments to its
/// operation.
/// </summary>
/// <remarks>
/// It reads <see cref="XmlDocumentation"/>, so it needs <c>GenerateDocumentationFile</c> in the project that declares
/// the type or action. It only fills what is still empty, so a description from an attribute, from the native
/// XML-comment generator, or from another transformer wins.
/// </remarks>
internal sealed class XmlCommentTransformer : IOpenApiSchemaTransformer, IOpenApiOperationTransformer
{
    // The generator's metadata keys: a schema with a schema id moves into components and its property gets a $ref,
    // which takes its description from the ref-description entry.
    private const string _SchemaIdKey = "x-schema-id";
    private const string _RefDescriptionKey = "x-ref-description";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var propertySummary = context.JsonPropertyInfo?.AttributeProvider is MemberInfo member
            ? XmlDocumentation.Summary(member)
            : null;

        if (_IsComponent(schema))
        {
            // Every property that refers to the component shares its schema, and the generator keeps whichever
            // instance it met first, so each instance carries the type's summary and the property's own comment goes
            // on its reference.
            if (!string.IsNullOrEmpty(propertySummary))
            {
                schema.Metadata!.TryAdd(_RefDescriptionKey, propertySummary);
            }

            _DescribeType(schema, context.JsonTypeInfo.Type);
        }
        else if (!string.IsNullOrEmpty(propertySummary))
        {
            if (string.IsNullOrEmpty(schema.Description))
            {
                schema.Description = propertySummary;
            }
        }
        else if (context.JsonPropertyInfo is null)
        {
            _DescribeType(schema, context.JsonTypeInfo.Type);
        }

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var method = context.Description.ActionDescriptor is ControllerActionDescriptor action
            ? action.MethodInfo
            : context.Description.ActionDescriptor.EndpointMetadata.OfType<MethodInfo>().FirstOrDefault();

        if (method is null)
        {
            return Task.CompletedTask;
        }

        operation.Summary ??= XmlDocumentation.Summary(method);
        operation.Description ??= XmlDocumentation.Remarks(method);

        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (!string.IsNullOrEmpty(parameter.Description) || parameter.Name is null)
            {
                continue;
            }

            // Query names are camelCased in the document; the C# parameter keeps its own casing.
            var name = method
                .GetParameters()
                .FirstOrDefault(x => string.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                ?.Name;

            if (name is not null)
            {
                parameter.Description = XmlDocumentation.Parameter(method, name);
            }
        }

        return Task.CompletedTask;
    }

    private static bool _IsComponent(OpenApiSchema schema)
    {
        return schema.Metadata?.TryGetValue(_SchemaIdKey, out var id) == true && id is string { Length: > 0 };
    }

    private static void _DescribeType(OpenApiSchema schema, Type type)
    {
        if (string.IsNullOrEmpty(schema.Description) && !_IsWireValueWrapper(type))
        {
            schema.Description = XmlDocumentation.Summary(type);
        }
    }

    // A primitive is sent as its underlying value, so its type comment describes a .NET wrapper a client never sees.
    private static bool _IsWireValueWrapper(Type type) => PrimitiveSchemaTransformer.IsPrimitive(type);
}
