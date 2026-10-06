// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Resources;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.OpenApi.Nswag;

/// <summary>
/// Documents the missing-tenant 403 alongside the standard permission-denied 403, allowing API documentation
/// and generated SDKs to reflect both shapes permitted by <see cref="ForbiddenProblemDetails"/>.
/// </summary>
public sealed class TenantRequiredExampleOperationProcessor : IOperationProcessor
{
    private const string _DefaultExampleName = "forbidden";
    private const string _TenantRequiredExampleName = "tenantRequired";

    /// <inheritdoc />
    public bool Process(OperationProcessorContext context)
    {
        Argument.IsNotNull(context);

        if (context is not AspNetCoreOperationProcessorContext aspNet)
        {
            return true;
        }

        var metadata = aspNet.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (
            metadata.OfType<IAllowAnonymous>().Any()
            || metadata.LastOrDefault(x => x is RequireTenantAttribute or AllowMissingTenantAttribute)
                is not RequireTenantAttribute
        )
        {
            return true;
        }

        if (
            !context.OperationDescription.Operation.Responses.TryGetValue(
                OpenApiStatusCodes.Forbidden,
                out var forbidden
            )
        )
        {
            return true;
        }

        foreach (var mediaType in forbidden.Content.Values)
        {
            if (mediaType.Examples is not { } examples)
            {
                continue;
            }

            if (mediaType.Example is { } existing)
            {
                examples.TryAdd(
                    _DefaultExampleName,
                    new OpenApiExample { Summary = "Permission denied", Value = existing }
                );

                mediaType.Example = null;
            }

            examples.TryAdd(
                _TenantRequiredExampleName,
                new OpenApiExample { Summary = "Tenant context required", Value = _CreateTenantRequiredExample() }
            );
        }

        return true;
    }

    // English on purpose: NSwag caches the generated document, so text resolved under the culture of
    // whichever request generated it first would be served to every reader.
    private const string _TenantRequiredDescription =
        "An operation required an ambient tenant context but none was set.";

    private static Dictionary<string, object?> _CreateTenantRequiredExample()
    {
        return new(StringComparer.Ordinal)
        {
            ["type"] = HeadlessProblemDetailsConstants.Types.Forbidden,
            ["title"] = HeadlessProblemDetailsConstants.Titles.Forbidden,
            ["status"] = 403,
            ["detail"] = _TenantRequiredDescription,
            ["instance"] = "/some-endpoint",
            ["error"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = GeneralErrorCodes.TenantRequired,
                ["description"] = _TenantRequiredDescription,
            },
            ["traceId"] = "<trace-id>",
            ["buildNumber"] = "<version>",
            ["commitNumber"] = "<commit>",
            ["timestamp"] = "2024-01-01T12:00:00+00:00",
        };
    }
}
