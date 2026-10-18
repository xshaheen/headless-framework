// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Renders each <see cref="HttpContractMetadata"/> on the endpoint: its request headers as header parameters, its
/// responses as operation responses, and its response headers on every documented response in their status range.
/// </summary>
/// <remarks>
/// The responses start without content; <see cref="ProblemDetailsTransformer"/>, which runs later, types each error
/// response with its Headless problem-details schema. An entry the operation already has keeps its own description: a
/// bound header parameter only becomes required when the metadata requires it, and a declared response or response
/// header is left alone.
/// </remarks>
internal sealed class HttpContractTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var contract in context.Description.GetEndpointMetadata().OfType<HttpContractMetadata>())
        {
            Apply(operation, contract);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Renders one contract on the operation, for a transformer that derives a contract from the application's
    /// services rather than from endpoint metadata.
    /// </summary>
    public static void Apply(OpenApiOperation operation, HttpContractMetadata contract)
    {
        _AddRequestHeaders(operation, contract.RequestHeaders);
        _AddResponses(operation, contract.Responses);
        _AddResponseHeaders(operation, contract.ResponseHeaders);
    }

    private static void _AddRequestHeaders(
        OpenApiOperation operation,
        IReadOnlyList<HttpRequestHeaderDescription> headers
    )
    {
        if (headers.Count == 0)
        {
            return;
        }

        var parameters = operation.Parameters ??= [];

        foreach (var header in headers)
        {
            // A repeated name and location makes the document invalid, so a header the action binds itself is reused.
            var declared = parameters.FirstOrDefault(parameter =>
                parameter is { In: ParameterLocation.Header }
                && string.Equals(parameter.Name, header.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (declared is null)
            {
                parameters.Add(
                    new OpenApiParameter
                    {
                        Name = header.Name,
                        In = ParameterLocation.Header,
                        Required = header.Required,
                        Description = header.Description,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    }
                );
            }
            else if (header.Required && declared is OpenApiParameter concrete)
            {
                concrete.Required = true;
            }
        }
    }

    private static void _AddResponses(OpenApiOperation operation, IReadOnlyList<HttpResponseDescription> responses)
    {
        if (responses.Count == 0)
        {
            return;
        }

        var documented = operation.Responses ??= [];

        foreach (var response in responses)
        {
            documented.TryAdd(
                response.StatusCode.ToString(CultureInfo.InvariantCulture),
                new OpenApiResponse { Description = response.Description }
            );
        }
    }

    private static void _AddResponseHeaders(
        OpenApiOperation operation,
        IReadOnlyList<HttpResponseHeaderDescription> headers
    )
    {
        if (headers.Count == 0 || operation.Responses is not { Count: > 0 } responses)
        {
            return;
        }

        foreach (var (statusCode, response) in responses)
        {
            // "default" and the "4XX" range keys carry no single status, so no header range can claim them.
            if (
                response is not OpenApiResponse concrete
                || !int.TryParse(statusCode, NumberStyles.None, CultureInfo.InvariantCulture, out var status)
            )
            {
                continue;
            }

            foreach (var header in headers)
            {
                if (status < header.MinStatusCode || status > header.MaxStatusCode)
                {
                    continue;
                }

                var responseHeaders = concrete.Headers ??= new Dictionary<string, IOpenApiHeader>(
                    StringComparer.OrdinalIgnoreCase
                );

                if (
                    responseHeaders.Keys.Any(name =>
                        string.Equals(name, header.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    continue;
                }

                responseHeaders[header.Name] = new OpenApiHeader
                {
                    Description = header.Description,
                    Required = header.Required,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                };
            }
        }
    }
}
