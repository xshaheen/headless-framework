// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.Http;
using Headless.MultiTenancy.Resources;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Documents what pre-authentication tenant catalog resolution adds to an endpoint: the tenant header it reads and the
/// rejections it writes before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// It runs only when the registered <see cref="TenantCatalogResolutionDescription"/> says resolution is configured,
/// and skips endpoints marked with
/// <see cref="SkipTenantResolutionAttribute"/>. Each configured header becomes an optional header parameter; the single
/// header becomes required when the endpoint requires a tenant and no other identifier source can supply one.
/// </para>
/// <para>
/// The rejections follow <c>TenantCatalogRejectionWriter</c>: 400 <c>g:tenant_identifier_invalid</c> always; then
/// either the collapsed 404 <c>g:tenant_resolution_failed</c>, or with detailed errors 404 <c>g:tenant_unknown</c> and
/// 403 <c>g:tenant_disabled</c> or <c>g:tenant_identifier_mismatch</c>. Each rejection carries
/// <c>Cache-Control: no-store</c>. It runs after <see cref="ProblemDetailsTransformer"/>: a response the operation
/// already documents keeps its description and its example as one named example beside the tenant ones, and a response
/// only the rejection produces gets the tenant examples alone. The standard 404 is already documented on every operation
/// unless <see cref="HeadlessOpenApiOptions.StandardProblemResponses"/> is off, so the tenant-only 404 appears only then.
/// </para>
/// </remarks>
internal sealed class TenantResolutionTransformer : IOpenApiOperationTransformer
{
    private const string _CacheControlHeader = "Cache-Control";

    // English on purpose: a document can be cached and served to every reader, so text resolved under the culture of
    // whichever request generated it first would reach readers of every other culture.
    private const string _IdentifierInvalid = "The tenant identifier is invalid.";
    private const string _ResolutionFailed = "The requested tenant could not be resolved.";
    private const string _Unknown = "No tenant matches the requested identifier.";
    private const string _Disabled = "This tenant is disabled.";

    private const string _IdentifierMismatch =
        "The resolved tenant identifier does not match the authenticated tenant.";

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var description = context.Description;

        if (
            description.GetEndpointMetadata().OfType<SkipTenantResolutionAttribute>().Any()
            || context.ApplicationServices.GetService<TenantCatalogResolutionDescription>()
                is not { IsConfigured: true } resolution
        )
        {
            return;
        }

        var rejections = _GetRejections(resolution.DetailedResolutionErrors);

        foreach (var (statusCode, rejection) in rejections)
        {
            await _AddRejectionAsync(operation, context, statusCode, rejection, cancellationToken);
        }

        HttpContractTransformer.Apply(
            operation,
            new HttpContractMetadata
            {
                RequestHeaders = _GetRequestHeaders(resolution, description, context.ApplicationServices),
                ResponseHeaders =
                [
                    .. rejections.Select(rejection => new HttpResponseHeaderDescription(
                        _CacheControlHeader,
                        int.Parse(rejection.StatusCode, CultureInfo.InvariantCulture),
                        "no-store when the tenant catalog rejected the request."
                    )),
                ],
            }
        );
    }

    private static List<HttpRequestHeaderDescription> _GetRequestHeaders(
        TenantCatalogResolutionDescription resolution,
        ApiDescription description,
        IServiceProvider services
    )
    {
        var names = resolution.HeaderNames;

        if (names.Count == 0)
        {
            return [];
        }

        // With several names, or another source that can supply the identifier, no single header is mandatory.
        var required =
            names.Count == 1
            && !resolution.HasNonHeaderSource
            && EndpointAuthorization.RequiresTenant(description, services);

        var text =
            names.Count == 1
                ? "The identifier of the tenant the request targets, resolved through the tenant catalog before authentication."
                : "The identifier of the tenant the request targets, resolved through the tenant catalog before "
                    + $"authentication. Send at most one of {string.Join(", ", names)}: a request that sends several is "
                    + "rejected with 400.";

        return [.. names.Select(name => new HttpRequestHeaderDescription(name, required, text))];
    }

    private static List<(string StatusCode, Rejection Rejection)> _GetRejections(bool detailed)
    {
        List<(string, Rejection)> rejections =
        [
            (
                OpenApiStatusCodes.BadRequest,
                new Rejection(
                    "Bad Request - The tenant identifier is invalid.",
                    "badRequest",
                    "Malformed request",
                    // Shape validation fails with its own detail: the tenant identifier is malformed, not the body.
                    [
                        new RejectionExample(
                            "tenantIdentifierInvalid",
                            "Tenant identifier invalid",
                            _IdentifierInvalid,
                            TenancyErrorCodes.IdentifierInvalid,
                            _IdentifierInvalid
                        ),
                    ]
                )
            ),
        ];

        if (!detailed)
        {
            // Unknown, disabled, and mismatched tenants collapse into one 404 so a caller cannot enumerate tenants.
            rejections.Add(
                (
                    OpenApiStatusCodes.NotFound,
                    new Rejection(
                        "Not Found - The tenant could not be resolved.",
                        "notFound",
                        "Resource not found",
                        [
                            new RejectionExample(
                                "tenantResolutionFailed",
                                "Tenant not resolved",
                                Detail: null,
                                TenancyErrorCodes.ResolutionFailed,
                                _ResolutionFailed
                            ),
                        ]
                    )
                )
            );

            return rejections;
        }

        rejections.Add(
            (
                OpenApiStatusCodes.NotFound,
                new Rejection(
                    "Not Found - No tenant matches the identifier.",
                    "notFound",
                    "Resource not found",
                    [
                        new RejectionExample(
                            "tenantUnknown",
                            "Tenant unknown",
                            Detail: null,
                            TenancyErrorCodes.Unknown,
                            _Unknown
                        ),
                    ]
                )
            )
        );
        rejections.Add(
            (
                OpenApiStatusCodes.Forbidden,
                new Rejection(
                    "Forbidden - The tenant is disabled, or does not match the authenticated user's tenant.",
                    "forbidden",
                    "Permission denied",
                    [
                        new RejectionExample(
                            "tenantDisabled",
                            "Tenant disabled",
                            Detail: null,
                            TenancyErrorCodes.Disabled,
                            _Disabled
                        ),
                        new RejectionExample(
                            "tenantIdentifierMismatch",
                            "Tenant identifier mismatch",
                            Detail: null,
                            TenancyErrorCodes.IdentifierMismatch,
                            _IdentifierMismatch
                        ),
                    ]
                )
            )
        );

        return rejections;
    }

    private static async Task _AddRejectionAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        string statusCode,
        Rejection rejection,
        CancellationToken cancellationToken
    )
    {
        var responses = operation.Responses ??= [];
        var existed = responses.TryGetValue(statusCode, out var documented);

        if (!existed)
        {
            var created = new OpenApiResponse { Description = rejection.Description };
            responses[statusCode] = created;
            await ProblemDetailsTransformer.DescribeAsync(statusCode, created, context, cancellationToken);
            documented = created;
        }

        if (
            documented?.Content is not { } content
            || !content.TryGetValue(ContentTypes.Applications.ProblemJson, out var mediaType)
            || _BaseExample(mediaType) is not { } template
            // A 400 the endpoint declared as a validation problem has no error member, so its schema cannot carry the
            // tenant example; the response still documents the status.
            || _IsValidationProblem(mediaType)
        )
        {
            return;
        }

        var examples = mediaType.Examples ??= new Dictionary<string, IOpenApiExample>(StringComparer.Ordinal);

        if (existed && mediaType.Example is { } own)
        {
            // OpenAPI forbids example and examples together, so the existing one moves under a name.
            examples.TryAdd(
                rejection.DefaultExampleName,
                new OpenApiExample { Summary = rejection.DefaultSummary, Value = own }
            );
        }

        mediaType.Example = null;

        foreach (var example in rejection.Examples)
        {
            var value = (JsonObject)template.DeepClone();

            if (example.Detail is { } detail)
            {
                value["detail"] = detail;
            }

            value["error"] = ProblemDetailsTransformer.Error(example.Code, example.ErrorDescription);
            examples.TryAdd(example.Name, new OpenApiExample { Summary = example.Summary, Value = value });
        }
    }

    // The template is the generic example of the status: the tenant examples differ from it only in detail and error.
    private static JsonObject? _BaseExample(OpenApiMediaType mediaType)
    {
        if (mediaType.Example is JsonObject example)
        {
            return example;
        }

        return mediaType.Examples?.Values.Select(example => example.Value).OfType<JsonObject>().FirstOrDefault();
    }

    private static bool _IsValidationProblem(OpenApiMediaType mediaType)
    {
        return mediaType.Schema is OpenApiSchemaReference { Reference.Id: nameof(BadRequestValidationProblemDetails) };
    }

    private sealed record Rejection(
        string Description,
        string DefaultExampleName,
        string DefaultSummary,
        IReadOnlyList<RejectionExample> Examples
    );

    private sealed record RejectionExample(
        string Name,
        string Summary,
        string? Detail,
        string Code,
        string ErrorDescription
    );
}
