// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds the responses the server writes before the handler runs, when it cannot read the request: 400 for an operation
/// that binds a body or a route, query, header, or form value; 413 for an operation with a request body; and for an
/// endpoint that validates an antiforgery token, the token header and its 400.
/// </summary>
/// <remarks>
/// <para>
/// A value that fails to bind raises <c>BadHttpRequestException</c>, which the Headless exception handler writes as a
/// 400 problem; a body over the server's size limit raises it with 413. An endpoint with
/// <see cref="IRequestSizeLimitMetadata"/> that lifts the limit gets no 413, and one that sets a limit names it. An
/// invalid antiforgery token ends the request with 400, which the status-codes rewriter turns into a problem. The token
/// header is optional: the form field <see cref="AntiforgeryOptions.FormFieldName"/> may carry the token instead.
/// </para>
/// <para>
/// Every response starts without content; <see cref="ProblemDetailsTransformer"/> types it later. A status the
/// operation already documents keeps its own description.
/// </para>
/// </remarks>
internal sealed class RequestReadingResponseTransformer : IOpenApiOperationTransformer
{
    private static readonly BindingSource[] _BoundSources =
    [
        BindingSource.Path,
        BindingSource.Query,
        BindingSource.Header,
        BindingSource.Body,
        BindingSource.Form,
        BindingSource.FormFile,
        BindingSource.ModelBinding,
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var description = context.Description;
        var reasons = new List<string>(2);

        if (_BindsRequest(description))
        {
            reasons.Add("a parameter or the body is missing, malformed, or of the wrong type");
        }

        if (_GetAntiforgeryOptions(description, context.ApplicationServices) is { } antiforgery)
        {
            reasons.Add("the antiforgery token is missing or invalid");

            if (!string.IsNullOrWhiteSpace(antiforgery.HeaderName))
            {
                HttpContractTransformer.Apply(
                    operation,
                    new HttpContractMetadata
                    {
                        RequestHeaders =
                        [
                            new HttpRequestHeaderDescription(
                                antiforgery.HeaderName,
                                required: false,
                                $"The antiforgery request token. Send it in this header or in the {antiforgery.FormFieldName} form field."
                            ),
                        ],
                    }
                );
            }
        }

        if (reasons.Count > 0)
        {
            (operation.Responses ??= []).TryAdd(
                OpenApiStatusCodes.BadRequest,
                new OpenApiResponse
                {
                    Description = $"Bad Request - The request could not be read: {string.Join("; or ", reasons)}.",
                }
            );
        }

        if (operation.RequestBody is not null && _HasBodySizeLimit(description, out var limit))
        {
            (operation.Responses ??= []).TryAdd(
                OpenApiStatusCodes.PayloadTooLarge,
                new OpenApiResponse
                {
                    Description = limit is { } bytes
                        ? $"Payload Too Large - The request body is larger than {bytes.ToString(CultureInfo.InvariantCulture)} bytes."
                        : "Payload Too Large - The request body is larger than the server accepts.",
                }
            );
        }

        return Task.CompletedTask;
    }

    private static bool _BindsRequest(ApiDescription description)
    {
        return description.ParameterDescriptions.Any(parameter =>
            parameter.Source is { } source && Array.Exists(_BoundSources, bound => bound == source)
        );
    }

    // The innermost metadata wins, as it does for the antiforgery middleware: DisableAntiforgery() after a form
    // parameter turns validation off.
    private static AntiforgeryOptions? _GetAntiforgeryOptions(ApiDescription description, IServiceProvider services)
    {
        var metadata = description.ActionDescriptor.EndpointMetadata.OfType<IAntiforgeryMetadata>().LastOrDefault();

        if (metadata is not { RequiresValidation: true } || services.GetService<IAntiforgery>() is null)
        {
            return null;
        }

        return services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
    }

    // False when the endpoint lifts the limit; otherwise the endpoint's own limit, or null when the server's applies.
    private static bool _HasBodySizeLimit(ApiDescription description, out long? limit)
    {
        var metadata = description
            .ActionDescriptor.EndpointMetadata.OfType<IRequestSizeLimitMetadata>()
            .LastOrDefault();
        limit = metadata?.MaxRequestBodySize;

        return metadata is null || limit is not null;
    }
}
