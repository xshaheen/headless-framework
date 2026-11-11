// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Describes an endpoint that carries <see cref="LiveUpdatesMetadata"/> as a live operation: the
/// <c>x-headless-live</c> extension names the stream formats it answers, and its success response gains one media
/// type per format.
/// </summary>
/// <remarks>
/// <para>
/// OpenAPI 3.1 has no keyword for the items of a stream, so each stream media type's schema describes one item, the
/// reading that later versions of the specification spell <c>itemSchema</c>. For <c>text/event-stream</c> the item is
/// the data of a <c>result</c> event, which is the body of the single response, so it takes that schema unchanged.
/// For <c>application/x-ndjson</c> the item is one line: a result carrying the same schema, a failure carrying a
/// problem-details body, or a heartbeat.
/// </para>
/// <para>
/// The formats are the two every live endpoint answers; a format an application adds with its own transport is not
/// known here. A success response without a JSON body leaves the result untyped. A stream media type the endpoint
/// declared itself is kept.
/// </para>
/// </remarks>
internal sealed class LiveUpdatesTransformer : IOpenApiOperationTransformer
{
    private const string _ExtensionName = "x-headless-live";
    private const string _EventStream = "text/event-stream";
    private const string _Ndjson = "application/x-ndjson";

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (!context.Description.GetEndpointMetadata().OfType<LiveUpdatesMetadata>().Any())
        {
            return;
        }

        var extensions = operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        extensions[_ExtensionName] = new JsonNodeExtension(
            new JsonObject { ["mediaTypes"] = new JsonArray(_EventStream, _Ndjson) }
        );

        if (_FindSuccessResponse(operation) is not { } success)
        {
            return;
        }

        var content = success.Content ??= new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal);
        var result = content.TryGetValue(ContentTypes.Applications.Json, out var json) ? json.Schema : null;

        IOpenApiSchema failure = await context.GetOrCreateSchemaAsync(
            typeof(HeadlessProblemDetails),
            parameterDescription: null,
            cancellationToken
        );

        if (context.Document is { } document)
        {
            failure = OpenApiSchemaComponents.Resolve(document, failure);
        }

        content.TryAdd(_EventStream, new OpenApiMediaType { Schema = result });
        content.TryAdd(_Ndjson, new OpenApiMediaType { Schema = _Line(result, failure) });
    }

    // A stream starts only from a run that answered 200 to 299, and an endpoint documents one such response; the
    // lowest is taken so the choice does not depend on declaration order when it documents more.
    private static OpenApiResponse? _FindSuccessResponse(OpenApiOperation operation)
    {
        if (operation.Responses is not { Count: > 0 } responses)
        {
            return null;
        }

        OpenApiResponse? success = null;
        var lowest = int.MaxValue;

        foreach (var (statusCode, response) in responses)
        {
            if (
                response is OpenApiResponse concrete
                && int.TryParse(statusCode, NumberStyles.None, CultureInfo.InvariantCulture, out var status)
                && status is >= 200 and < 300
                && status < lowest
            )
            {
                success = concrete;
                lowest = status;
            }
        }

        return success;
    }

    private static OpenApiSchema _Line(IOpenApiSchema? result, IOpenApiSchema failure)
    {
        return new OpenApiSchema
        {
            OneOf = [_Update("result", result), _Update("failure", failure), _Kind("heartbeat")],
        };
    }

    private static OpenApiSchema _Update(string kind, IOpenApiSchema? value)
    {
        var line = _Kind(kind);
        line.Required!.Add("version");
        line.Required.Add("value");
        line.Properties!["version"] = new OpenApiSchema { Type = JsonSchemaType.String };

        // Without a schema the member is still required and may hold any JSON value.
        if (value is not null)
        {
            line.Properties["value"] = value;
        }

        return line;
    }

    private static OpenApiSchema _Kind(string kind)
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(StringComparer.Ordinal) { "kind" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["kind"] = new OpenApiSchema { Type = JsonSchemaType.String, Const = kind },
            },
        };
    }
}
