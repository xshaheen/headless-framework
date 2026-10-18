// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Gives every operation an operation id, so generated clients get stable method names.</summary>
/// <remarks>
/// The native generator sets an id only from a route or endpoint name. This fills the rest: an MVC action gets the
/// name of its HTTP method attribute when one is set, else <c>{Controller}_{Action}</c> without an <c>Async</c>
/// suffix; an unnamed Minimal API endpoint gets its HTTP method followed by its path segments. A literal segment
/// contributes its words, and a segment that is only a parameter contributes <c>By</c> and the parameter name, without
/// its constraint, default, optional, or catch-all markers: <c>GET /orders/{orderId:guid}/items/{itemId?}</c> becomes
/// <c>GetOrdersByOrderIdItemsByItemId</c>. A parameter that shares a segment with literal text contributes nothing, so
/// <c>GET /v{version}/things/{id}</c> becomes <c>GetVThingsById</c>: such a parameter is a prefix every operation of a
/// group shares (an API version, a culture), and naming it would lengthen every id without telling two apart, while a
/// whole-segment parameter is what separates <c>GetThingsById</c> from the <c>GetThings</c> list. Duplicates are
/// numbered once the document is complete, as a last resort.
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
            : description.GetEndpointMetadata().OfType<IRouteNameMetadata>().FirstOrDefault()?.RouteName
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

        foreach (var segment in _ParseSegments(relativePath))
        {
            var literalStart = builder.Length;

            foreach (var literal in segment.Parts.OfType<RoutePatternLiteralPart>())
            {
                _AppendWords(builder, literal.Content);
            }

            if (builder.Length > literalStart)
            {
                continue;
            }

            foreach (var parameter in segment.Parts.OfType<RoutePatternParameterPart>())
            {
                builder.Append("By");
                _AppendWords(builder, parameter.Name);
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<RoutePatternPathSegment> _ParseSegments(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return [];
        }

        try
        {
            return RoutePatternFactory.Parse(relativePath).PathSegments;
        }
        catch (RoutePatternException)
        {
            // The path came from a route pattern, so this only guards a hand-built description; its literal
            // segments still name the operation.
            return
            [
                .. relativePath
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Where(segment => segment.IndexOfAny(['{', '}']) < 0)
                    .Select(segment => RoutePatternFactory.Segment(RoutePatternFactory.LiteralPart(segment))),
            ];
        }
    }

    private static void _AppendWords(StringBuilder builder, string text)
    {
        foreach (var word in text.Split(_WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            _AppendPascal(builder, word);
        }
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
