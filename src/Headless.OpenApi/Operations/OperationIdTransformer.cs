// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Gives every operation an operation id, so generated clients get stable method names.</summary>
/// <remarks>
/// The native generator sets an id only from a route or endpoint name. This fills the rest: an MVC action gets the
/// name of its HTTP method attribute when one is set, else <c>{Controller}_{Action}</c> without an <c>Async</c>
/// suffix; an unnamed Minimal API endpoint gets its HTTP method followed by the literal text of its path segments
/// (<c>GET /orders/{id}/items</c> becomes <c>GetOrdersItems</c>, <c>GET /v{version}/things</c> becomes
/// <c>GetVThings</c>). Duplicates are numbered once the document is complete.
/// </remarks>
internal sealed class OperationIdTransformer : IOpenApiOperationTransformer
{
    private static readonly char[] _WordSeparators = ['-', '_', '.', '~'];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrEmpty(operation.OperationId))
        {
            return Task.CompletedTask;
        }

        var description = context.Description;

        operation.OperationId = description.ActionDescriptor is ControllerActionDescriptor action
            ? _GetHttpAttributeName(action, description.HttpMethod)
                ?? $"{action.ControllerName}_{_TrimAsyncSuffix(action.ActionName)}"
            : description.ActionDescriptor.EndpointMetadata.OfType<IRouteNameMetadata>().FirstOrDefault()?.RouteName
                ?? _FromRoute(description.HttpMethod, description.RelativePath);

        return Task.CompletedTask;
    }

    private static string? _GetHttpAttributeName(ControllerActionDescriptor action, string? httpMethod)
    {
        return action
            .MethodInfo.GetCustomAttributes<HttpMethodAttribute>()
            .FirstOrDefault(attribute =>
                !string.IsNullOrWhiteSpace(attribute.Name)
                && (httpMethod is null || attribute.HttpMethods.Contains(httpMethod, StringComparer.OrdinalIgnoreCase))
            )
            ?.Name;
    }

    private static string _TrimAsyncSuffix(string actionName)
    {
        return actionName.EndsWith("Async", StringComparison.Ordinal) ? actionName[..^"Async".Length] : actionName;
    }

    private static string _FromRoute(string? httpMethod, string? relativePath)
    {
        var builder = new StringBuilder();
        _AppendPascal(builder, (httpMethod ?? "Get").ToLowerInvariant());

        foreach (var segment in (relativePath ?? string.Empty).Split(['/', '\\']))
        {
            // A parameter can share a segment with literal text (v{version:apiVersion}); only the literal names it.
            var parameterStart = segment.IndexOfAny(['{', '[']);
            var literal = parameterStart < 0 ? segment : segment[..parameterStart];

            foreach (var word in literal.Split(_WordSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                _AppendPascal(builder, word);
            }
        }

        return builder.ToString();
    }

    private static void _AppendPascal(StringBuilder builder, string word)
    {
        var start = builder.Length;

        foreach (var character in word)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(builder.Length == start ? char.ToUpperInvariant(character) : character);
            }
        }
    }
}
