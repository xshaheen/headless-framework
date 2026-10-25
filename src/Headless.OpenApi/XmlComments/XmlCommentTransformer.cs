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
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrEmpty(schema.Description))
        {
            return Task.CompletedTask;
        }

        if (context.JsonPropertyInfo?.AttributeProvider is MemberInfo member)
        {
            schema.Description = XmlDocumentation.Summary(member);
        }
        else if (!_IsWireValueWrapper(context.JsonTypeInfo.Type))
        {
            schema.Description = XmlDocumentation.Summary(context.JsonTypeInfo.Type);
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

    // A primitive is sent as its underlying value, so its type comment describes a .NET wrapper a client never sees.
    private static bool _IsWireValueWrapper(Type type) => PrimitiveSchemaTransformer.IsPrimitive(type);
}
