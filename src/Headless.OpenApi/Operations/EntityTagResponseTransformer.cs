// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Documents the <c>ETag</c> response header on every 2xx response whose body type implements
/// <see cref="IHasEntityTag"/>.
/// </summary>
/// <remarks>
/// The Headless entity-tag profiles (<c>AddHeadlessMvcEntityTagConcurrency()</c>, or
/// <c>AddHeadlessMinimalApiEntityTagConcurrency()</c> with <c>.WithEntityTag()</c>) write the header for such a body on
/// any 2xx response. The document cannot see whether the profile is on, so the body type alone decides; the header is
/// therefore not marked required.
/// </remarks>
internal sealed class EntityTagResponseTransformer : IOpenApiOperationTransformer
{
    private const string _HeaderName = "ETag";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (operation.Responses is not { Count: > 0 } responses)
        {
            return Task.CompletedTask;
        }

        foreach (var responseType in context.Description.SupportedResponseTypes)
        {
            if (
                responseType is not { StatusCode: >= 200 and < 300, Type: { } type }
                || !typeof(IHasEntityTag).IsAssignableFrom(type)
                || !responses.TryGetValue(
                    responseType.StatusCode.ToString(CultureInfo.InvariantCulture),
                    out var response
                )
                || response is not OpenApiResponse concrete
            )
            {
                continue;
            }

            var headers = concrete.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);

            if (headers.Keys.Any(name => string.Equals(name, _HeaderName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            headers[_HeaderName] = new OpenApiHeader
            {
                Description =
                    "The strong entity tag of the returned representation. Send it back in If-Match to update it.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };
        }

        return Task.CompletedTask;
    }
}
