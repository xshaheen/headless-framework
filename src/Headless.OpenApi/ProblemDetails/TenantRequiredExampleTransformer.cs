// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.Api.Resources;
using Headless.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Documents the missing-tenant 403 beside the permission-denied 403 on endpoints that require a tenant, so
/// documentation and generated clients show both shapes <see cref="ForbiddenProblemDetails"/> allows.
/// </summary>
/// <remarks>
/// It runs after <see cref="ProblemDetailsTransformer"/> and derives the tenant example from the 403 example that
/// transformer wrote, so the two differ only in <c>detail</c> and <c>error</c>. The single example moves into a named
/// <c>forbidden</c> example beside <c>tenantRequired</c>, because OpenAPI forbids <c>example</c> and <c>examples</c>
/// together. An endpoint requires a tenant when its effective tenancy metadata is <see cref="RequireTenantAttribute"/>, or
/// when an applied default or fallback policy holds <see cref="TenantRequirement"/> and the endpoint does not allow a
/// missing tenant. Optional-tenant and anonymous endpoints keep the single example.
/// </remarks>
internal sealed class TenantRequiredExampleTransformer : IOpenApiOperationTransformer
{
    private const string _DefaultExampleName = "forbidden";
    private const string _TenantRequiredExampleName = "tenantRequired";

    // English on purpose: a document can be cached and served to every reader, so text resolved under the culture of
    // whichever request generated it first would reach readers of every other culture.
    private const string _TenantRequiredDescription = "This operation requires a tenant, but none was set.";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var description = context.Description;

        if (
            !EndpointAuthorization.RequiresTenant(description, context.ApplicationServices)
            || operation.Responses is null
            || !operation.Responses.TryGetValue(OpenApiStatusCodes.Forbidden, out var forbidden)
            || forbidden.Content is not { } content
            || !content.TryGetValue(ContentTypes.Applications.ProblemJson, out var mediaType)
            || mediaType.Example is not JsonObject permissionDenied
        )
        {
            return Task.CompletedTask;
        }

        var tenantRequired = (JsonObject)permissionDenied.DeepClone();
        tenantRequired["detail"] = _TenantRequiredDescription;
        tenantRequired["error"] = ProblemDetailsTransformer.Error(
            GeneralErrorCodes.TenantRequired,
            _TenantRequiredDescription
        );

        var examples = mediaType.Examples ??= new Dictionary<string, IOpenApiExample>(StringComparer.Ordinal);
        examples.TryAdd(
            _DefaultExampleName,
            new OpenApiExample { Summary = "Permission denied", Value = permissionDenied }
        );
        examples.TryAdd(
            _TenantRequiredExampleName,
            new OpenApiExample { Summary = "Tenant context required", Value = tenantRequired }
        );
        mediaType.Example = null;

        return Task.CompletedTask;
    }
}
